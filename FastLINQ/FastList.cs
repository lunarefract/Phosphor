using System.Collections;

namespace FastLINQ
{
    public class FastList<T> : IEnumerable<T>
    {
        private class ListItem
        {
            public ListItem Next;
            public T item;
        }

        private ListItem _root = new();
        private ListItem _last = null;

        public T First
        {
            get
            {
                if (_root.Next != null) return _root.Next.item;
                else return default;
            }
        }
        public class Iterator
        {
            FastList<T> _ilist;

            private ListItem prev;
            private ListItem curr;

            internal Iterator(FastList<T> ll)
            {
                _ilist = ll;
                Reset();
            }

            public bool MoveNext(out T v)
            {
                ListItem ll = curr.Next;

                if (ll == null)
                {
                    v = default;
                    _ilist._last = curr;
                    return false;
                }

                v = ll.item;

                prev = curr;
                curr = ll;

                return true;
            }

            public void Remove()
            {
                if (_ilist._last.Equals(curr)) _ilist._last = prev;
                prev.Next = curr.Next;
            }

            public void Insert(T item)
            {
                var i = new ListItem()
                {
                    item = item,
                    Next = curr
                };
                if (prev == null)
                    _ilist._root.Next = i;
                else
                    prev.Next = i;
                //if (curr.Equals(_ilist.last))
                //{
                //    _ilist.last = curr;
                //}
            }

            public void Reset()
            {
                this.prev = null;
                this.curr = _ilist._root;
            }
        }

        public class FastIterator : IEnumerator<T>
        {
            FastList<T> _ilist;

            private ListItem curr;

            internal FastIterator(FastList<T> ll)
            {
                _ilist = ll;
                Reset();
            }

            public object Current => curr.item;
            T IEnumerator<T>.Current => curr.item;

            public void Dispose() { }

            public bool MoveNext()
            {
                try
                {
                    curr = curr.Next;
                    return curr != null;
                }
                catch { return false; }
            }

            public void Reset()
            {
                curr = _ilist._root;
            }
        }

        public void Add(T item)
        {
            ListItem li = new();
            li.item = item;

            if (_root.Next != null && _last != null)
            {
                while (_last.Next != null) _last = _last.Next;
                _last.Next = li;
            }
            else
            {
                _root.Next = li;
            }
            _last = li;
        }

        public T Pop()
        {
            ListItem el = _root.Next;
            _root.Next = el.Next;
            return el.item;
        }

        public Iterator Iterate()
        {
            return new Iterator(this);
        }

        public bool ZeroLen => _root.Next == null;

        public IEnumerator<T> FastIterate()
        {
            return new FastIterator(this);
        }

        public void Unlink()
        {
            _root.Next = null;
            _last = null;
        }

        public int Count()
        {
            int cnt = 0;
            ListItem li = _root.Next;
            while (li != null)
            {
                cnt++;
                li = li.Next;
            }
            return cnt;
        }

        public bool Any()
        {
            return _root.Next != null;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return FastIterate();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return FastIterate();
        }
    }
}