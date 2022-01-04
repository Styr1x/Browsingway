using Browsingway.Common;
using SharedMemory;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace Browsingway;

// whole IPC mechanism will be replaced, anyway
#nullable disable

public class IpcResponse<TResponse>
{
	public TResponse Data = default!;
	public bool Success;
}

public class IpcBuffer<TIncoming, TOutgoing> : RpcBuffer
{
	private static readonly BinaryFormatter _formatter = new() { Binder = new FuckingKillMeBinder() };

	public IpcBuffer(string name, Func<TIncoming, object> callback) : base(name, CallbackFactory(callback)) { }

	// Handle conversion between wire's byte[] and nicer clr types
	private static Func<ulong, byte[], byte[]> CallbackFactory(Func<TIncoming, object> callback)
	{
		return (messageId, rawRequest) =>
		{
			TIncoming request = Decode<TIncoming>(rawRequest);

			object response = callback(request);

			return response == null ? null : Encode(response);
		};
	}

	public async Task<IpcResponse<TResponse>> RemoteRequestAsync<TResponse>(TOutgoing request, int timeout = 5000)
	{
		byte[] rawRequest = Encode(request);
		RpcResponse rawResponse = await RemoteRequestAsync(rawRequest, timeout);
		return new IpcResponse<TResponse> { Success = rawResponse.Success, Data = rawResponse.Success ? Decode<TResponse>(rawResponse.Data) : default };
	}

	private static byte[] Encode<T>(T value)
	{
		byte[] encoded;
		using (MemoryStream stream = new())
		{
#pragma warning disable SYSLIB0011
			_formatter.Serialize(stream, value!);
#pragma warning restore SYSLIB0011
			encoded = stream.ToArray();
		}

		return encoded;
	}

	private static T Decode<T>(byte[] encoded)
	{
		if (encoded == null) { return default; }

		T value;
		using (MemoryStream stream = new(encoded))
		{
#pragma warning disable SYSLIB0011
			value = (T)_formatter.Deserialize(stream);
#pragma warning restore SYSLIB0011
		}

		return value;
	}
}

public sealed class FuckingKillMeBinder : SerializationBinder
{
	public override Type BindToType(string assemblyName, string typeName)
	{
		return Type.GetType($"{typeName}, {assemblyName}")!;
	}

	public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
	{
		base.BindToName(serializedType, out assemblyName, out typeName);
		if (serializedType.ToString().Contains("Browsingway.Common"))
		{
			assemblyName = typeof(DownstreamIpcRequest).Assembly.FullName;
		}
	}
}