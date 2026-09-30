using System;
using System.Collections;
using System.Collections.Generic;
using SPTarkov.DI;
using SPTarkov.Server.Core.DI;

namespace ModSource.Server;

public sealed class LoadStepList(IReadOnlyList<DependencyInjectionContainer> inner) : IReadOnlyList<DependencyInjectionContainer>
{
    private static readonly object Gate = new();
    private static Enumerator _tracking;

    public int Count => inner.Count;

    public DependencyInjectionContainer this[int index] => inner[index];

    public IEnumerator<DependencyInjectionContainer> GetEnumerator()
    {
        return ProvenanceTracker.Active ? new Enumerator(inner) : inner.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private sealed class Enumerator(IReadOnlyList<DependencyInjectionContainer> items) : IEnumerator<DependencyInjectionContainer>
    {
        private int _index = -1;
        private Type _open;

        public DependencyInjectionContainer Current => items[_index];

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            Close();
            _index++;

            if (_index >= items.Count)
            {
                Release();
                return false;
            }

            var step = items[_index];
            if (step.Type == typeof(IOnLoad) && ModSourceBootstrap.IsTrackedModType(step.ParentType) && Claim())
            {
                _open = step.ParentType;
                ProvenanceTracker.Begin(_open);
            }

            return true;
        }

        public void Reset()
        {
            Close();
            _index = -1;
        }

        public void Dispose()
        {
            Close();
            Release();
        }

        private void Close()
        {
            if (_open == null)
            {
                return;
            }

            ProvenanceTracker.End(_open);
            _open = null;
        }

        private bool Claim()
        {
            lock (Gate)
            {
                if (_tracking != null && _tracking != this)
                {
                    return false;
                }

                _tracking = this;
                return true;
            }
        }

        private void Release()
        {
            lock (Gate)
            {
                if (_tracking == this)
                {
                    _tracking = null;
                }
            }
        }
    }
}
