using System.Collections.Concurrent;
using System.Text;

namespace PhosphorMP.Utils
{
    public class FastLogger : IDisposable
    {
        private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
        private readonly Thread _worker;
        private readonly TextWriter _writer;
        private volatile bool _running = true;

        public FastLogger(string filePath = null)
        {
            _writer = filePath == null
                ? Console.Out
                : new StreamWriter(filePath, append: true, Encoding.UTF8, bufferSize: 8192) { AutoFlush = false };

            _worker = new Thread(ProcessQueue) { IsBackground = true };
            _worker.Start();
        }

        public void Write(string message)
        {
            if (_running)
                _queue.Add($"{DateTime.Now:O} {message}");
        }

        private void ProcessQueue()
        {
            foreach (var msg in _queue.GetConsumingEnumerable())
            {
                _writer.WriteLine(msg);
            }
        }

        public void Dispose()
        {
            _running = false;
            _queue.CompleteAdding();
            _worker.Join();
            _writer.Flush();
            _writer.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public static class Log
    {
        private static readonly FastLogger Instance = new FastLogger();
        
        public static void WriteLine(string message)
        {
            Instance.Write(message + Environment.NewLine);
        }
        
        public static void Write(string message)
        {
            Instance.Write(message);
        }
        
        public static void Dispose()
        {
            Instance.Dispose();
        }
    }
}