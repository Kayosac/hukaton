#ifndef HUKATON_FOREST_BILLBOARD_INCLUDED
#define HUKATON_FOREST_BILLBOARD_INCLUDED

// Cylindrical billboard for far tree cards. The object's yaw and tilt are ignored: it stands upright on its pivot and
// turns about the vertical axis so that local +Z points at the camera (the concave side of the VG19 strip). Per-axis
// scale is kept. The basis comes from the object matrix, so this needs per-object matrices (no static batching).

void ForestBillboardBasis(out float3 pivotWS, out float3 scale, out float3 right, out float3 forward)
{
    pivotWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    scale = float3(length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)),
                   length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)),
                   length(float3(UNITY_MATRIX_M._m02, UNITY_MATRIX_M._m12, UNITY_MATRIX_M._m22)));
    float2 toCamera = GetCameraPositionWS().xz - pivotWS.xz;
    float len = length(toCamera);
    toCamera = len > 1e-4 ? toCamera / len : float2(0, 1);
    forward = float3(toCamera.x, 0, toCamera.y);
    right = float3(toCamera.y, 0, -toCamera.x);
}

float3 ForestBillboardPositionOS(float3 positionOS)
{
    float3 pivotWS, scale, right, forward;
    ForestBillboardBasis(pivotWS, scale, right, forward);
    float3 positionWS = pivotWS + right * (positionOS.x * scale.x) + float3(0, positionOS.y * scale.y, 0) + forward * (positionOS.z * scale.z);
    return TransformWorldToObject(positionWS);
}

float3 ForestBillboardDirectionOS(float3 directionOS)
{
    float3 pivotWS, scale, right, forward;
    ForestBillboardBasis(pivotWS, scale, right, forward);
    return TransformWorldToObjectDir(right * directionOS.x + float3(0, directionOS.y, 0) + forward * directionOS.z);
}

#endif
