internal class ConcurrencyController
{
    private readonly SemaphoreSlim _semaphore = new(0, int.MaxValue);
    private readonly object _targetDopLock = new();
    
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