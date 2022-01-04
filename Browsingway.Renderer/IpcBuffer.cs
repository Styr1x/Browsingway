using SharedMemory;
using System.Runtime.Serialization.Formatters.Binary;

namespace Browsingway.Renderer;

public class IpcResponse<TResponse>
{
	public TResponse? Data;
	public bool Success;
}

public class IpcBuffer<TIncoming, TOutgoing> : RpcBuffer
{
	// ReSharper disable once StaticMemberInGenericType
	private static readonly BinaryFormatter _formatter = new();

	public IpcBuffer(string name, Func<TIncoming?, object?> callback) : base(name, CallbackFactory(callback)) { }

	// Handle conversion between wire's byte[] and nicer clr types
	private static Func<ulong, byte[], byte[]?> CallbackFactory(Func<TIncoming?, object?> callback)
	{
		return (_, rawRequest) =>
		{
			TIncoming? request = Decode<TIncoming>(rawRequest);

			object? response = callback(request);

			return response == null ? null : Encode(response);
		};
	}

	public IpcResponse<TResponse> RemoteRequest<TResponse>(TOutgoing? request, int timeout = 5000)
	{
		byte[] rawRequest = Encode(request);
		RpcResponse? rawResponse = RemoteRequest(rawRequest, timeout);
		return new IpcResponse<TResponse> { Success = rawResponse.Success, Data = rawResponse.Success ? Decode<TResponse>(rawResponse.Data) : default };
	}

	public async Task<IpcResponse<TResponse>> RemoteRequestAsync<TResponse>(TOutgoing? request, int timeout = 5000)
	{
		byte[] rawRequest = Encode(request);
		RpcResponse? rawResponse = await RemoteRequestAsync(rawRequest, timeout);
		return new IpcResponse<TResponse> { Success = rawResponse.Success, Data = rawResponse.Success ? Decode<TResponse>(rawResponse.Data) : default };
	}

	private static byte[] Encode<T>(T value)
	{
		using MemoryStream stream = new();
#pragma warning disable SYSLIB0011
		_formatter.Serialize(stream, value!);
#pragma warning restore SYSLIB0011
		byte[] encoded = stream.ToArray();

		return encoded;
	}

	private static T? Decode<T>(byte[]? encoded)
	{
		if (encoded == null) { return default; }

		using MemoryStream stream = new(encoded);
#pragma warning disable SYSLIB0011
		T value = (T)_formatter.Deserialize(stream);
#pragma warning restore SYSLIB0011

		return value;
	}
}