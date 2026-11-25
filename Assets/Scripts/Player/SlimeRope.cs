using UnityEngine;

// Dibuja una cuerda de slime con un LineRenderer: varios puntos con una ondulacion suave que se
// mueve con el tiempo (como gelatina estirada). Las puntas quedan fijas; el centro ondula mas.
// La textura que se repite la pone el material del LineRenderer (modo Tile).
public static class SlimeRope
{
    const int Segments = 16;
    const float Amplitude = 0.07f;   // que tanto ondula (unidades)
    const float WaveLength = 2.2f;   // largo de cada onda (unidades)
    const float Speed = 6f;          // que tan rapido se mueve la onda

    public static void Draw(LineRenderer line, Vector3 from, Vector3 to)
    {
        if (line == null) return;
        Vector3 dir = to - from;
        float length = dir.magnitude;
        if (length < 0.001f)
        {
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            return;
        }

        Vector3 normal = new Vector3(-dir.y, dir.x, 0f) / length;
        float t = Time.time * Speed;
        line.positionCount = Segments + 1;
        for (int i = 0; i <= Segments; i++)
        {
            float u = (float)i / Segments;
            float envelope = Mathf.Sin(u * Mathf.PI);              // 0 en las puntas, 1 al centro
            float wave = Mathf.Sin(u * length / WaveLength * Mathf.PI * 2f - t);
            line.SetPosition(i, from + dir * u + normal * (wave * envelope * Amplitude));
        }
    }
}
