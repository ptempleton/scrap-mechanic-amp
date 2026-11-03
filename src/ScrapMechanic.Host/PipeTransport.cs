using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using ScrapMechanic.Host.Core;

namespace ScrapMechanic.Host;

public sealed record Request(string Command);

// Bounded, length-prefixed UTF-8 JSON; no line-based unbounded reads from clients.
public static class Wire
{
    public const int MaximumBytes = 65536;
    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken cancellation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, ConfigJson.Options);
        if (payload.Length > MaximumBytes) throw new InvalidDataException("IPC frame exceeds limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellation);
        await stream.WriteAsync(payload, cancellation);
        await stream.FlushAsync(cancellation);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid IPC frame size.");
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, cancellation);
        return JsonSerializer.Deserialize<T>(bytes, ConfigJson.Options) ?? throw new InvalidDataException("Empty IPC message.");
    }
}

[SupportedOSPlatform("windows")]
public static class PipeTransport
{
    public static PipeSecurity Security(string ampIdentitySid)
    {
        var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No Windows identity.");
        var amp = new SecurityIdentifier(ampIdentitySid);
        if (amp.IsWellKnown(WellKnownSidType.WorldSid) || amp.IsWellKnown(WellKnownSidType.AuthenticatedUserSid) ||
            amp.IsWellKnown(WellKnownSidType.BuiltinUsersSid) || amp.IsWellKnown(WellKnownSidType.AnonymousSid) ||
            amp.IsWellKnown(WellKnownSidType.NetworkSid)) throw new InvalidDataException("AMP identity must be a specific account, not a broad group.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(owner);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        if (amp != owner)
            security.AddAccessRule(new PipeAccessRule(amp, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return security;
    }

    public static NamedPipeServerStream CreateServer(string name, PipeSecurity security, bool first) =>
        NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 8, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
            4096, 4096, security, HandleInheritability.None);

    public static async Task<Reply> SendAsync(ClientConfiguration configuration, string command)
    {
        configuration.Validate();
        using var pipe = new NamedPipeClientStream(".", configuration.PipeName,
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification, HandleInheritability.None);
        using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(connect.Token);
        // Reject a pipe squatted by another local identity before sending a command.
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (owner?.Value != new SecurityIdentifier(configuration.AgentIdentitySid).Value)
            throw new UnauthorizedAccessException("Pipe server owner does not match AgentIdentitySid.");
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds));
        await Wire.WriteAsync(pipe, new Request(command), request.Token);
        return await Wire.ReadAsync<Reply>(pipe, request.Token);
    }

    public static async Task ServeAsync(AgentConfiguration configuration, HostController controller, IEventSink log, CancellationToken cancellation)
    {
        var security = Security(configuration.AmpIdentitySid);
        var listener = CreateServer(configuration.PipeName, security, true);
        var clients = new List<Task>();
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await listener.WaitForConnectionAsync(cancellation);
                var connected = listener;
                // Keep at least one pipe instance alive to prevent a name-squatting gap.
                clients.RemoveAll(t => t.IsCompleted);
                if (clients.Count >= 6) await Task.WhenAny(clients);
                listener = CreateServer(configuration.PipeName, security, false);
                clients.Add(HandleAsync(connected, controller, log));
            }
        }
        finally
        {
            listener.Dispose();
            await Task.WhenAll(clients);
        }
    }

    private static async Task HandleAsync(NamedPipeServerStream pipe, HostController controller, IEventSink log)
    {
        using (pipe)
        {
            try
            {
                using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var request = await Wire.ReadAsync<Request>(pipe, readTimeout.Token);
                if (request.Command is null || request.Command.Length > 128) throw new InvalidDataException("Invalid command.");
                // A client disconnect cannot cancel a WM_CLOSE/backup already in progress.
                var response = await controller.ExecuteAsync(request.Command);
                using var writeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Wire.WriteAsync(pipe, response, writeTimeout.Token);
            }
            catch (Exception ex) { log.Write("client_error", ex.Message, new { exception = ex.GetType().Name }); }
        }
    }
}
