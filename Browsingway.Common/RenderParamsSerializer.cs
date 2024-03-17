using Browsingway.Common.Ipc;
using FlatSharp;
using System;

namespace Browsingway.Common;

public static class RenderParamsSerializer
{
	public static string Serialize(RenderParams renderParams)
	{
		var maxSize = RenderParams.Serializer.GetMaxSize(renderParams);
		byte[] buffer = new byte[maxSize];
		int bytesWritten = RenderParams.Serializer.Write(buffer, renderParams);
		return Convert.ToBase64String(buffer, 0, bytesWritten);
	}

	public static RenderParams Deserialize(string base64)
	{
		byte[] buffer = Convert.FromBase64String(base64);
		return RenderParams.Serializer.Parse(buffer);
	}
}