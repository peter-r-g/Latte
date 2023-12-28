#version 450

// Input.
layout (location = 0) in vec2 inOffset;

// Output.
layout (location = 0) out vec4 outColor;

void main()
{
	float dis = sqrt(dot(inOffset, inOffset));
	if (dis >= 1.0)
		discard;

	outColor = vec4(1);
}