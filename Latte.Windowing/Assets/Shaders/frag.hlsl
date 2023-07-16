struct FSInput
{
	[[vk::location(0)]] float2 FragmentTextureCoordinates : TEXCOORD0;
};

struct FSOutput
{
    float4 Color : SV_TARGET0;
};

Texture2D texture2d : register( t1 );
SamplerState samplerState : register( s1 );

FSOutput main( FSInput input )
{
	FSOutput output = (FSOutput)0;
    output.Color = texture2d.Sample( samplerState, input.FragmentTextureCoordinates );
	
	return output;
}
