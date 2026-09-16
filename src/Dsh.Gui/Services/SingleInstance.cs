using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Dsh.Gui.Services;

/**
 * 单实例保护: 命名 Mutex 表示"某个实例持有锁"(进程退出时由 OS 自动释放, 不留陈旧状态),
 * 命名管道用于把"第二次启动"传给持锁实例让它显示窗口(Unix 上命名事件不可用, 管道两端都支持)。
 * 拿不到锁但管道也没人应答时(锁被别的程序/残留进程占着), 降级成"无单实例保护"照常启动, 绝不静默退出。
 */
public sealed class SingleInstance : IDisposable
{
    public const string ActivationMessage = "activate";
    private const string NamePrefix = "dsh-gui";
    private const int ConnectTimeoutMs = 500;

    private readonly Mutex? _mutex;
    private readonly NamedPipeServerStream? _pipe;
    private readonly CancellationTokenSource _cancellation = new();

    private SingleInstance(Mutex? mutex, NamedPipeServerStream? pipe)
    {
        _mutex = mutex;
        _pipe = pipe;
    }

    /** true = 本实例持有锁并监听激活通知; false = 降级实例。 */
    public bool HasLock => _mutex is not null;

    /** 持锁实例收到"第二次启动"时触发(后台线程); 视图自行切到 UI 线程处理。 */
    public event Action? ActivationRequested;

    public static SingleInstance? Acquire(string homePath)
    {
        var key = KeyForHome(homePath);
        var mutex = new Mutex(initiallyOwned: false, MutexName(key), out var createdNew);
        if (createdNew)
        {
            var pipe = CreatePipe(key);
            var instance = new SingleInstance(mutex, pipe);
            _ = instance.ListenAsync();
            return instance;
        }
        mutex.Dispose();
        if (TrySendActivation(key))
            return null;
        return new SingleInstance(null, null);
    }

    /** home 对应的名字后缀(哈希后只含十六进制字符, 可安全用于 mutex/管道名); 供诊断与测试使用。 */
    public static string KeyForHome(string homePath)
    {
        var normalized = Path.GetFullPath(homePath).Replace('\\', '/').ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /** 名字用当前会话/用户命名空间(默认), 不申请 Global\: 普通用户没有创建全局内核对象的特权。 */
    public static string MutexName(string key) => $"{NamePrefix}-{key}";

    public static string PipeName(string key) => $"{NamePrefix}-activate-{key}";

    public void Dispose()
    {
        _cancellation.Cancel();
        _pipe?.Dispose();
        _mutex?.Dispose();
        _cancellation.Dispose();
    }

    private static NamedPipeServerStream CreatePipe(string key)
        => new(PipeName(key), PipeDirection.In, maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private async Task ListenAsync()
    {
        var pipe = _pipe;
        if (pipe is null)
            return;
        var buffer = new byte[64];
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                var read = await pipe.ReadAsync(buffer, _cancellation.Token);
                if (read > 0 && Encoding.UTF8.GetString(buffer, 0, read) == ActivationMessage)
                    ActivationRequested?.Invoke();
                pipe.Disconnect();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    /** 连一次持锁实例的管道; 连不上说明"有锁主但没监听"(残留/他人占用), 调用方会降级启动。 */
    private static bool TrySendActivation(string key)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(key), PipeDirection.Out, PipeOptions.None);
            client.Connect(ConnectTimeoutMs);
            var payload = Encoding.UTF8.GetBytes(ActivationMessage);
            client.Write(payload, 0, payload.Length);
            client.Flush();
            return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
