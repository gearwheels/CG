﻿#version 150 core

in vec3 FragNormale;
in vec3 FragVertex;


uniform vec3 Ka_Material;
uniform vec3 Kd_Material;
uniform vec3 Ks_Material;
uniform float P_Material;
uniform vec3 Ia_Material;
uniform vec3 Il_Material;
uniform vec3 LightPos;
uniform vec2 Parameters;
uniform vec3 CameraPos;
uniform vec3 FragColor;

void main(void)
{
    vec3 L = vec3(LightPos.x - FragVertex.x, LightPos.y - FragVertex.y, LightPos.z - FragVertex.z);
    float dist = length(L);
    L = normalize(L);
    vec3 FragNormaleW = normalize(FragNormale);


    
    float I_red = Ia_Material.x * Ka_Material.x;
    float I_green = Ia_Material.y * Ka_Material.y;
    float I_blue = Ia_Material.z * Ka_Material.z;

    
    I_red += clamp(0, 1, Il_Material.x * Kd_Material.x * dot(L, FragNormaleW) / (Parameters[0] * dist + Parameters[1]));
    I_green += clamp(0, 1, Il_Material.y * Kd_Material.y * dot(L, FragNormaleW) / (Parameters[0] * dist + Parameters[1]));
    I_blue += clamp(0, 1, Il_Material.z * Kd_Material.z * dot(L, FragNormaleW) / (Parameters[0] * dist + Parameters[1]));

    
    if (dot(L, FragNormaleW) > 0) 
    {
        vec3 S = vec3(CameraPos.x - FragVertex.x, CameraPos.y - FragVertex.y, CameraPos.z - FragVertex.z);
        vec3 R = vec3(reflect(-L, FragNormale));
        
        S = normalize(S);
        R = normalize(R);

        if (dot(R, S) > 0)
        {
            I_red += clamp(0, 1, Il_Material.x * Ks_Material.x * pow(dot(R, S), P_Material) / (Parameters[0] * dist + Parameters[1]));
            I_green += clamp(0, 1, Il_Material.y * Ks_Material.y * pow(dot(R, S), P_Material) / (Parameters[0] * dist + Parameters[1]));
            I_blue += clamp(0, 1, Il_Material.z * Ks_Material.z * pow(dot(R, S), P_Material) / (Parameters[0] * dist + Parameters[1]));
        }
    }

    I_red = min(1, I_red);
    I_green = min(1, I_green);
    I_blue = min(1, I_blue);

    vec4 result = vec4(FragColor.x * I_red, FragColor.y * I_green, FragColor.z * I_blue, 1);

    gl_FragColor = result;
}