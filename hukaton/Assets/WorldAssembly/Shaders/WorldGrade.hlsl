// The endless world's place colour grading for what the presentation camera draws after the world's post-processing
// (water, ground fog, clouds, rain): WorldAtmosphere sets these globals from the place's look (the swamp's Volume),
// so transparent things take the same colour as the world round them. Off (all zero) when nothing is set.
#ifndef HUKATON_WORLD_GRADE_INCLUDED
#define HUKATON_WORLD_GRADE_INCLUDED

float _WorldGradeOn;
float4 _WorldGradeFilter;
float _WorldGradeSaturation;
// How much water reflects the fog instead of the sky probe's clear sky (thick air: the swamp, rain).
float _WorldReflectFog;

half3 WorldGrade(half3 colour)
{
    if (_WorldGradeOn < 0.001) return colour;
    half grey = dot(colour, half3(0.2126, 0.7152, 0.0722));
    half3 graded = lerp(grey.xxx, colour, _WorldGradeSaturation) * _WorldGradeFilter.rgb;
    return lerp(colour, graded, _WorldGradeOn);
}

#endif
