using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// One connection lifetime. Each direction has a single worker; Unity only handles queued frames.
internal sealed class CompanionPipe : IDisposable
{
    const int MaxBytes = 64 * 1024;
    readonly PipeStream stream;
    readonly BlockingCollection<string> outgoing = new BlockingCollection<string>(32);
    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    int stopped;
    public bool Stopped => Volatile.Read(ref stopped) == 2;

    public CompanionPipe(string name, bool server)
    {
        stream = server
            ? (PipeStream)new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
            : new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task.Run(ReadLoop);
    }

    void ReadLoop()
    {
        try {
            if (stream is NamedPipeServerStream server) server.WaitForConnection();
            else ((NamedPipeClientStream)stream).Connect(1500);
            _ = Task.Run(WriteLoop);
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) {
                while (!Stopped) {
                    int size = reader.ReadInt32();
                    if (size <= 0 || size > MaxBytes || incoming.Count >= 32) break;
                    var bytes = reader.ReadBytes(size);
                    if (bytes.Length != size) break;
                    incoming.Enqueue(Encoding.UTF8.GetString(bytes));
                }
            }
        } catch (IOException) { } catch (InvalidOperationException) { } catch (TimeoutException) { }
        finally { Dispose(); }
    }

    void WriteLoop()
    {
        try {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
                foreach (var frame in outgoing.GetConsumingEnumerable()) {
                    byte[] bytes = Encoding.UTF8.GetBytes(frame);
                    if (bytes.Length > MaxBytes) break;
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    writer.Flush();
                }
            }
        } catch (IOException) { } catch (InvalidOperationException) { }
        finally { Dispose(); }
    }

    public bool Send(string frame)
    {
        try { if (!Stopped && outgoing.TryAdd(frame)) return true; }
        catch (InvalidOperationException) { }
        Dispose();
        return false;
    }

    public bool Receive(out string frame) => incoming.TryDequeue(out frame);

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref stopped, 1, 0) != 0) return;
        outgoing.CompleteAdding();
        stream.Dispose();
        Volatile.Write(ref stopped, 2);
    }
}
