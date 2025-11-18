using UnityEngine;

public static class RsColorSpaceHelper
{
    public static void RgbToHsv(byte r, byte g, byte b, out Vector3 hsv)
    {
        float R = r / 255f;
        float G = g / 255f;
        float B = b / 255f;

        float max = (R > G) ? R : G;
        max = (max > B) ? max : B;

        float min = (R < G) ? R : G;
        min = (min < B) ? min : B;

        float delta = max - min;

        float h = 0f;
        float s = 0f;
        float v = max; // V (Value)

        if (max > 0.0001f)
        {
            s = delta / max; // S (Saturation)
        }
        else
        {
            s = 0f;
            h = 0f;
            hsv.x = h;
            hsv.y = s;
            hsv.z = v;
            return;
        }

        if (delta > 0.0001f)
        {
            if (R >= max)
                h = (G - B) / delta;
            else if (G >= max)
                h = 2.0f + (B - R) / delta;
            else
                h = 4.0f + (R - G) / delta;

            h *= 60.0f;

            if (h < 0.0f)
                h += 360.0f;

            h /= 360.0f;
        }
        else
        {
            h = 0f;
        }

        hsv.x = h;
        hsv.y = s;
        hsv.z = v;
    }
}
