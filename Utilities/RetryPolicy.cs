using System;
using System.Threading;
using System.Threading.Tasks;

namespace ViaManager.Utilities
{
    public static class RetryPolicy
    {
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T>> action,
            int maxAttempts = 3,
            int initialDelayMs = 500,
            int maxDelayMs = 5000,
            double backoffMultiplier = 2.0,
            Func<Exception, bool>? shouldRetry = null,
            Action<int, Exception, TimeSpan>? onRetry = null,
            CancellationToken cancellationToken = default)
        {
            int attempt = 0;
            var random = new Random();
            int currentDelayMs = initialDelayMs;

            while (true)
            {
                attempt++;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await action(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (attempt >= maxAttempts || (shouldRetry != null && !shouldRetry(ex)))
                    {
                        throw;
                    }

                    // Add jitter: +/- 20%
                    double jitter = (random.NextDouble() * 0.4) + 0.8;
                    int delayWithJitter = (int)(currentDelayMs * jitter);
                    var delay = TimeSpan.FromMilliseconds(Math.Min(delayWithJitter, maxDelayMs));

                    onRetry?.Invoke(attempt, ex, delay);

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    currentDelayMs = (int)(currentDelayMs * backoffMultiplier);
                }
            }
        }

        public static async Task ExecuteWithRetryAsync(
            Func<CancellationToken, Task> action,
            int maxAttempts = 3,
            int initialDelayMs = 500,
            int maxDelayMs = 5000,
            double backoffMultiplier = 2.0,
            Func<Exception, bool>? shouldRetry = null,
            Action<int, Exception, TimeSpan>? onRetry = null,
            CancellationToken cancellationToken = default)
        {
            await ExecuteWithRetryAsync<bool>(async ct =>
            {
                await action(ct).ConfigureAwait(false);
                return true;
            }, maxAttempts, initialDelayMs, maxDelayMs, backoffMultiplier, shouldRetry, onRetry, cancellationToken).ConfigureAwait(false);
        }
    }
}
