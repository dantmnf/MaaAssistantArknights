using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MaaWpfGui.Helper
{
    internal static class TaskExtensions
    {
#if !NET6_0_OR_GREATER
        public static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken ct)
        {
            static async Task<T2> WaitCancellationWithType<T2>(CancellationToken _ct)
            {
                await Task.Delay(Timeout.Infinite, _ct);
                throw new TaskCanceledException(); // unreachable
            }

            // used to cancel the delay task when the base task completed
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var delayTask = WaitCancellationWithType<T>(cts.Token);
            var completedTask = await Task.WhenAny(task, delayTask);

            // cancel the delay task (if not cancelled)
            cts.Cancel();

            // if the task completed, returns its result.
            // if the CancellationToken triggered, it will be the cancelled delayTask.
            return await completedTask;
        }
#endif
    }
}
