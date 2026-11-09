#ifndef HUKATON_FOREST_WIND_INCLUDED
#define HUKATON_FOREST_WIND_INCLUDED

// Forest wind: per-vertex amplitudes are baked into UV2 by the art pipeline.
//   UV2.x = sway amplitude in metres (0 at roots / hang points, grows toward crown tips and loose ends)
//   UV2.y = flutter amplitude in metres (small fast leaf / cloth jitter)
// Meshes without UV2 read (0,0) and stay perfectly still, so the shader is safe on static parts.
// Every object gets its own phase from its pivot, so neighbouring trees never move in lockstep.

float3 ForestWindObjectPosition(float3 positionOS, float2 windUV)
{
    float sway = windUV.x * _WindStrength;
    float flutter = windUV.y * _FlutterStrength;
    if (sway + flutter <= 1e-5)
        return positionOS;

    float3 pivotWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float3 positionWS = TransformObjectToWorld(positionOS);

    float2 dirXZ = _WindDirection.xz;
    dirXZ = dirXZ / max(length(dirXZ), 1e-4);
    float3 dir = float3(dirXZ.x, 0, dirXZ.y);
    float3 side = float3(-dir.z, 0, dir.x);

    float phase = dot(pivotWS.xz, float2(0.173, 0.131));
    float t = _Time.y * _WindSpeed;
    // slow gusts * two incommensurate waves: reads as wind, not a metronome
    float gust = 0.6 + 0.4 * sin(t * 0.31 + phase * 0.7) * sin(t * 0.13 + phase * 1.9);
    float along = (sin(t + phase) * 0.62 + sin(t * 2.13 + phase * 1.37) * 0.38) * gust + 0.3 * gust;
    float across = sin(t * 1.41 + phase * 2.3) * 0.35 * gust;
    float3 offset = (dir * along + side * across) * sway;
    // keep branches roughly their length: tips dip a little as they swing
    offset.y -= (abs(along) + abs(across)) * sway * 0.18;

    float f = sin(_Time.y * _FlutterSpeed + dot(positionWS, float3(3.7, 2.9, 3.3)))
            * (0.6 + 0.4 * sin(_Time.y * _FlutterSpeed * 0.53 + phase * 3.1));
    offset += float3(0.45, 1.0, 0.35) * f * flutter;

    return TransformWorldToObject(positionWS + offset);
}

#endif
