// Какая-то странная смесь семафора и барьера.
// Проблема: необходимо ограничивать число потоков, выполняющих работу до N, где N - переменное число, меняющееся прямо в рантайме.
// Стандартный семафор способен обеспечить только константный уровень параллелизма, операции "приостановить часть работающих потоков" - нет.
// Пример использования:
// В воркерах: while(true) { _controller.Pass(); ...work } 
// Альтернативно-экзотически: while(true) { _controller.Pass(); ...work; _controller.Pass(); ...work; ...etc }, 
// тогда каждый вызов Pass будет своеобразным breakpoint'ом, на котором поток в любой момент сможет остановиться.
// То есть потоки могуть встать посередине операции, но при этом число потоков выполняющих блоки между Pass() всегда будет стремиться к N.
// В тредах, желающих ограничить уровень параллелизма в воркерах: просто _controller.SetTargetDegreeOfParallelism(N);

// TODO: метод Exit(), позволяющий потоку выйти из контроллера (пометить себя неактивным), не заблокировавшись.
// while(!completed) { _controller.Pass(); ...work } _controller.Exit();

// Завязан на ThreadLocal, нельзя допускать смены потока.
// lock-free гарантии в тех случаях, когда N >= числа потоков в критической секции

internal class ConcurrencyController
{
    private readonly SemaphoreSlim _semaphore = new(0, int.MaxValue);
    private readonly object _targetDopLock = new();
    
    // Возможная оптимизация: упаковать в один long
    private volatile State _currentState;
    
    public ConcurrencyController(int initialTargetDegreeOfParallelism)
    {
        _currentState = new State(initialTargetDegreeOfParallelism, 0);
    }

    private readonly ThreadLocal<bool> _performingWork = new();

    public void Pass()
    {
        if (!_performingWork.IsValueCreated)
            _performingWork.Value = false;

        SpinWait spinner = new();
        
        while (true)
        {
            var localCurrentState = _currentState;
            
            // Поток прямо сейчас выполняет работу
            if (_performingWork.Value)
            {
                if (localCurrentState.ActiveThreads <= localCurrentState.TargetDegreeOfParallelism)
                {
                    if (localCurrentState == _currentState)
                    {
                        // Все норм, мы в квоте, просто проходим дальше.
                        return;
                    }
                    
                    // Состояние поменяли у нас перед носом, считаем лучше актуальное на следующем круге.
                    spinner.SpinOnce();
                    continue;
                }
                
                // Тредов больше, чем нужно, пробуем уменьшить счетчик и остановить текущий поток.
                var newState = localCurrentState with { ActiveThreads = localCurrentState.ActiveThreads - 1 };

                if (Interlocked.CompareExchange(ref _currentState, newState, localCurrentState) == localCurrentState)
                {
                    _performingWork.Value = false;
                    
                    // Получилось, останавливаемся и ждем, пока целевое количество снова увеличат.
                    _semaphore.Wait();
                    spinner.Reset();
                    
                    // Нам снова дали шанс бороться за право выполгять работу, идем пробовать
                    continue;
                }
                
                // В следующий раз повезет
                spinner.SpinOnce();
                continue;
            }
            
            // Поток только что прошел через семафор или вызвал метод первый раз
            if (localCurrentState.ActiveThreads < localCurrentState.TargetDegreeOfParallelism)
            {
                // Тредов меньше, чем нужно, пробуем увеличить счетчик и позволить этому потоку пройти.
            
                var updatedState = localCurrentState with { ActiveThreads = localCurrentState.ActiveThreads + 1 };
            
                if (Interlocked.CompareExchange(ref _currentState, updatedState, localCurrentState) == localCurrentState)
                {
                    // Получилось, 
                    _performingWork.Value = true;
                    return;
                }
                
                // Не получилось, уходим на второй круг
                spinner.SpinOnce();
            }
            else
            {
                // Мы оказались не нужны :(
                // Подождем следующего шанса.
                _semaphore.Wait();
                spinner.Reset();
            }
        }
    }
   
    public void SetTargetDegreeOfParallelism(int targetDegreeOfParallelism)
    {
        lock (_targetDopLock)
        {
            SpinWait spinner = new();
            
            while (true)
            {
                var localCurrentState = _currentState;
                
                var desiredState = localCurrentState with { TargetDegreeOfParallelism = targetDegreeOfParallelism };

                if (Interlocked.CompareExchange(ref _currentState, desiredState, localCurrentState) == localCurrentState)
                {
                    var delta = targetDegreeOfParallelism - localCurrentState.TargetDegreeOfParallelism;

                    if (delta > 0)
                        _semaphore.Release(delta);
                    
                    return;
                }
                
                spinner.SpinOnce();
            }
        }
    }

    public int GetTargetDegreeOfParallelism()
    {
        lock (_targetDopLock)
        {
            return _currentState.TargetDegreeOfParallelism;
        }
    }
    
    private record State(int TargetDegreeOfParallelism, int ActiveThreads);
}
