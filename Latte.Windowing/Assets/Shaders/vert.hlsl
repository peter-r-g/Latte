struct VSInput
{
	[[vk::location(0)]] float3 Position : POSITION0;
	[[vk::location(2)]] float2 TextureCoordinates : TEXCOORD0;
	
	[[vk::location(3)]] float3 InstancePosition : POSITION1;
};

struct VSOutput
{
	float4 Position : SV_POSITION;
	[[vk::location(0)]] float2 FragmentTextureCoordinates : TEXCOORD0;
};

struct UniformBufferObject
{
	float4x4 View;
	float4x4 Projection;
};

cbuffer ubo : register( b0, space0 )
{
	UniformBufferObject ubo;
};

VSOutput main( VSInput input )
{
	VSOutput output = (VSOutput) 0;
    output.FragmentTextureCoordinates = input.TextureCoordinates;
	
	float4 locPos = float4( input.Position.xyz, 1.0f );
	float4 pos = float4( locPos.xyz + input.InstancePosition, 1.0f );

	output.Position = mul( ubo.Projection, mul( ubo.View, pos ) );
	
	return output;
}
