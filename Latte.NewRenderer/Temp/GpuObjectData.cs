﻿using System.Numerics;

namespace Latte.NewRenderer.Temp;

internal struct GpuObjectData
{
	internal Matrix4x4 ModelMatrix;

	internal GpuObjectData( Matrix4x4 modelMatrix )
	{
		ModelMatrix = modelMatrix;
	}
}
