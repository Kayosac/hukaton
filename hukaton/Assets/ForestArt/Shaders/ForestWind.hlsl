#ifndef HUKATON_FOREST_WIND_INCLUDED
#define HUKATON_FOREST_WIND_INCLUDED

// Forest wind. The art pipeline bakes per-vertex amplitudes (metres) into UV2, offset by a mark:
//   UV2.x = FOREST_WIND_MARK + sway     (0 at roots / hang points, grows toward crown tips and loose ends)
//   UV2.y = FOREST_WIND_MARK + flutter  (small fast leaf / cloth jitter)
// The mark is a guard, not decoration: when a mesh has no UV2, Unity feeds its UV0 into TEXCOORD1, so plain
// amplitudes would make every mesh without wind data drift by its texture coordinates. Only marked values move;
// anything else (no UV2, zeros, ordinary UVs) stays perfectly still.
// Each object takes its phase from its pivot, so neighbouring trees never move in lockstep; all parts of one prefab
// share the pivot, so bark, crown and leaves of one tree always move together.

#define FOREST_WIND_MARK 1000.0

float2 ForestWindAmplitude(float2 windUV)
{
    float2 a = windUV - FOREST_WIND_MARK;
    bool marked = all(a > -0.001) && all(a < 1.0);
    return marked ? max(a, 0.0) : float2(0.0, 0.0);
}

float3 ForestWindObjectPosition(float3 positionOS, float2 windUV)
{
    float2 amp = ForestWindAmplitude(windUV);
    float sway = amp.x * _WindStrength;
    float flutter = amp.y * _FlutterStrength;
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
    // tips travel on an arc: a small drop that grows with the square of the swing (smooth through the rest pose)
    offset.y -= (along * along + across * across) * sway * 0.12;

    float f = sin(_Time.y * _FlutterSpeed + dot(positionWS, float3(3.7, 2.9, 3.3)))
            * (0.6 + 0.4 * sin(_Time.y * _FlutterSpeed * 0.53 + phase * 3.1));
    offset += float3(0.45, 1.0, 0.35) * f * flutter;

    return TransformWorldToObject(positionWS + offset);
}

#endif
