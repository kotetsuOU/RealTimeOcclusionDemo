using UnityEngine;

public static class RsYCbCrConverter
{
    // ITU-R BT.601
    public static void RgbToYCbCr(byte r, byte g, byte b, out Vector3Int ycbcr)
    {
        float fr = (float)r;
        float fg = (float)g;
        float fb = (float)b;

        // Y  =  0.299R + 0.587G + 0.114B
        // Cb = -0.169R - 0.331G + 0.500B + 128
        // Cr =  0.500R - 0.419G - 0.081B + 128

        int y = (int)(0.2990f * fr + 0.5870f * fg + 0.1140f * fb);
        int cb = (int)(-0.1687f * fr - 0.3313f * fg + 0.5000f * fb + 128);
        int cr = (int)(0.5000f * fr - 0.4187f * fg - 0.0813f * fb + 128);

        ycbcr = new Vector3Int(
            Mathf.Clamp(y, 0, 255),
            Mathf.Clamp(cb, 0, 255),
            Mathf.Clamp(cr, 0, 255)
        );
    }
}