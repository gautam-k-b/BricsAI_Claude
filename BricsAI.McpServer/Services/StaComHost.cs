using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BricsAI.McpServer.Services
{
    /// <summary>
    /// Hosts a single dedicated STA thread with a real Win32 message pump (via a hidden,
    /// never-shown Form) so that all COM automation against BricsCAD runs on one consistent
    /// apartment-threaded context. Every ComClient call must be routed through InvokeAsync —
    /// nothing outside this class should ever touch the COM `dynamic` objects directly.
    /// </summary>
    public sealed class StaComHost : IDisposable
    {
        private readonly Thread _thread;
        private Form? _pumpForm;
        private readonly ManualResetEventSlim _ready = new(false);
        private bool _disposed;

        public StaComHost()
        {
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "BricsAI-STA-COM"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();
        }

        private void RunMessageLoop()
        {
            _pumpForm = new Form
            {
                ShowInTaskbar = false,
                WindowState = FormWindowState.Minimized,
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                Opacity = 0
            };
            // Force native handle creation now, before releasing the constructor.
            var _ = _pumpForm.Handle;
            _ready.Set();
            Application.Run();
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pumpForm!.BeginInvoke(new Action(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }));
            return tcs.Task;
        }

        public Task InvokeAsync(Action action) => InvokeAsync<object?>(() =>
        {
            action();
            return null;
        });

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _pumpForm?.Invoke(new Action(Application.ExitThread));
            }
            catch { }
            _thread.Join(2000);
            _ready.Dispose();
        }
    }
}
