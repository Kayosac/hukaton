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

// Reactions: what touches the plants. Hakaton.Feel.PlantReactions uploads these globals every frame of an outing;
// never set (the shop, the editor) they are zeros and nothing moves. Both read the vertex's WORLD position, never the
// pivot: a chunk's still plants are merged into one mesh per material, whose pivot is the chunk's, not a plant's
// (the wind above then takes one phase for the whole chunk; its flutter already goes by world position).
//   benders: bodies standing in the plants - what is near leans away from them and is pressed down a little
//   shakes:  impacts - what is near shivers for a moment
// Both are scaled by the same baked amplitudes as the wind, so roots, trunks' feet and unmarked meshes stay put.
#define HK_BENDER_COUNT 6
#define HK_SHAKE_COUNT 4
float4 _HkBenders[HK_BENDER_COUNT];      // xyz = the body's feet (world), w = radius in metres; 0 = unused
float4 _HkBenderParams[HK_BENDER_COUNT]; // x = strength 0..1
float4 _HkShakes[HK_SHAKE_COUNT];        // xyz = the impact (world), w = radius in metres
float4 _HkShakeState[HK_SHAKE_COUNT];    // x = amplitude 0..1 (the envelope runs in C#), y = phase in radians

// Baked sway is the wind's travel in metres (a few centimetres: grass tips .04-.08, crowns .09-.14), far less than a
// trodden blade leans, so a lean is that many times the sway, capped so a tall blade leans instead of stretching.
#define HK_BEND_GAIN 5.0
#define HK_BEND_MAX 0.35
#define HK_SHAKE_SWAY_GAIN 1.2
#define HK_SHAKE_FLUTTER_GAIN 2.5
#define HK_SHAKE_MAX 0.12

// World offset in metres for a vertex at positionWS with baked amplitudes amp (x = sway, y = flutter).
float3 ForestReactionOffset(float3 positionWS, float2 amp)
{
    float3 offset = float3(0.0, 0.0, 0.0);

    float lean = min(amp.x * HK_BEND_GAIN, HK_BEND_MAX);
    float2 push = float2(0.0, 0.0);
    float press = 0.0;
    for (int b = 0; b < HK_BENDER_COUNT; b++)
    {
        float4 body = _HkBenders[b];
        float strength = _HkBenderParams[b].x;
        if (body.w > 0.001 && strength > 0.001)
        {
            float2 away = positionWS.xz - body.xz;
            float dist = length(away);
            float reach = saturate(1.0 - dist / body.w);
            // a body reaches from under its feet (a sprite's pivot may sit above the ground) to about its own height
            float rise = positionWS.y - body.y;
            float w = reach * reach * (3.0 - 2.0 * reach) * strength
                    * smoothstep(-1.3, -0.7, rise) * (1.0 - smoothstep(1.0, 1.6, rise));
            // right under the body there is no "away": only the press is left there, so nothing flips side to side
            push += away / max(dist, 1e-3) * (w * saturate(dist / (body.w * 0.2)));
            press += w;
        }
    }
    float pushed = min(length(push), 1.0);
    push *= pushed / max(length(push), 1e-4);
    offset.xz = push * lean;
    // the tip travels on an arc, and what is underfoot is squashed
    offset.y = -lean * (0.3 * pushed * pushed + 0.15 * saturate(press));

    // Shiver: a slow part that neighbouring vertices share (bark and its leaves go together, the far side of a crown
    // goes another way) on the sway amplitude, and a fine part that differs leaf to leaf on the flutter amplitude.
    float2 slow = float2(dot(positionWS, float3(1.1, 0.8, 1.4)), dot(positionWS, float3(-1.3, 0.9, 1.0)));
    float2 fine = float2(dot(positionWS, float3(11.3, 7.9, 9.1)), dot(positionWS, float3(-8.7, 12.1, 10.3)));
    float2 shiver = float2(0.0, 0.0);
    float2 rustle = float2(0.0, 0.0);
    for (int k = 0; k < HK_SHAKE_COUNT; k++)
    {
        float2 state = _HkShakeState[k].xy;
        if (state.x > 0.001)
        {
            float4 hit = _HkShakes[k];
            float reach = saturate(1.0 - distance(positionWS, hit.xyz) / max(hit.w, 1e-3));
            float w = reach * reach * (3.0 - 2.0 * reach) * state.x;
            shiver += sin(state.y * float2(1.0, 1.31) + slow) * w;
            rustle += sin(state.y * float2(1.73, 2.11) + fine) * w;
        }
    }
    offset.xz += clamp(shiver, -1.0, 1.0) * min(amp.x * HK_SHAKE_SWAY_GAIN, HK_SHAKE_MAX)
               + clamp(rustle, -1.0, 1.0) * min(amp.y * HK_SHAKE_FLUTTER_GAIN, HK_SHAKE_MAX);
    return offset;
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

    // Bodies and impacts add to the wind. A material with its wind turned off stays still for them too; one with the
    // wind turned up does not lean further.
    offset += ForestReactionOffset(positionWS, amp * saturate(float2(_WindStrength, _FlutterStrength)));

    return TransformWorldToObject(positionWS + offset);
}

#endif
