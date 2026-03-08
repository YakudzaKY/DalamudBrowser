using DalamudBrowser.Interop;
using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace DalamudBrowser.Rendering;

internal sealed class BrowserThreadDispatcher : IDisposable
{
    private const uint WakeMessage = NativeMethods.WmApp + 1;

    private readonly ConcurrentQueue<Action> queue = new();
    private readonly ManualResetEventSlim ready = new();
    private readonly Thread thread;
    private readonly DispatcherSynchronizationContext synchronizationContext;

    private bool disposed;
    private uint threadId;
    private Exception? startupException;

    public BrowserThreadDispatcher(string name)
    {
        synchronizationContext = new DispatcherSynchronizationContext(this);
        thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = name,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        ready.Wait();
        if (startupException != null)
        {
            ExceptionDispatchInfo.Capture(startupException).Throw();
        }
    }

    public bool IsDispatcherThread => Environment.CurrentManagedThreadId == thread.ManagedThreadId;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Enqueue(() => NativeMethods.PostQuitMessage(0));
        if (thread.IsAlive)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        ready.Dispose();
    }

    public void Post(Action action)
    {
        if (disposed)
        {
            return;
        }

        Enqueue(action);
    }

    private void Enqueue(Action action)
    {
        queue.Enqueue(action);
        if (threadId != 0)
        {
            NativeMethods.PostThreadMessage(threadId, WakeMessage, 0, 0);
        }
    }

    private void ThreadMain()
    {
        try
        {
            threadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        }
        catch (Exception ex)
        {
            startupException = ex;
            ready.Set();
            return;
        }

        ready.Set();

        while (true)
        {
            var result = NativeMethods.GetMessage(out var message, 0, 0, 0);
            if (result == -1)
            {
                break;
            }

            if (result == 0)
            {
                DrainQueue();
                break;
            }

            if (message.MessageId != WakeMessage)
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }

            DrainQueue();
        }

        DrainQueue();
    }

    private void DrainQueue()
    {
        while (queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch
            {
            }
        }
    }

    private sealed class DispatcherSynchronizationContext(BrowserThreadDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            dispatcher.Post(() => d(state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (dispatcher.IsDispatcherThread)
            {
                d(state);
                return;
            }

            using var signal = new ManualResetEventSlim();
            Exception? exception = null;
            dispatcher.Post(() =>
            {
                try
                {
                    d(state);
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    signal.Set();
                }
            });

            signal.Wait();
            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
