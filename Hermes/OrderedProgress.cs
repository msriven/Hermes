using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hermes;

internal sealed class OrderedProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Report(T value)
    {
        if (_context is null)
            handler(value);
        else
            _context.Post(static s =>
            {
                var (h, v) = ((Action<T>, T))s!;
                h(v);
            }, (handler, value));
    }
}
