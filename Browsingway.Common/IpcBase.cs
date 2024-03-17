using Browsingway.Common.Ipc;
using FlatSharp;
using SharedMemory;
using System;
using System.Threading.Tasks;

namespace Browsingway.Common;

public class IpcBase : IDisposable
{
	private readonly RpcBuffer _buffer;

	protected IpcBase(string name)
	{
		_buffer = new RpcBuffer(name, (msgId, data) =>
		{
			HandleCall(RpcCall.Serializer.Parse(data));
		});
	}
	
	protected async Task SendCall(IFlatBufferSerializable msg)
	{
		int maxSize = msg.Serializer.GetMaxSize(msg);
		byte[] buffer = new byte[maxSize];
		int bytesWritten = msg.Serializer.Write(buffer, msg);
		await _buffer.RemoteRequestAsync(buffer[..bytesWritten]);
	}
	
	protected async Task<T?> SendCall<T>(IFlatBufferSerializable msg) where T : class, IFlatBufferSerializable, new()
	{
		int maxSize = msg.Serializer.GetMaxSize(msg);
		byte[] buffer = new byte[maxSize];
		int bytesWritten = msg.Serializer.Write(buffer, msg);
		var response = await _buffer.RemoteRequestAsync(buffer[..bytesWritten]);
		if (!response.Success)
			return null;

		T result = new T();
		return (T)result.Serializer.Parse(response.Data);
	}

	protected virtual void HandleCall(RpcCall call)
	{
	}

	public void Dispose()
	{
		_buffer.Dispose();
	}
}