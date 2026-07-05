package org.libsdl.app;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.util.Log;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;

public class SDLHapticHandler_API26 extends SDLHapticHandler {
    @Override
    public void run(int device_id, float intensity, int length) {
        SDLHaptic haptic = getHaptic(device_id);
        if (haptic != null) {
            Log.d("SDL", "Rtest: Vibe with intensity " + intensity + " for " + length);
            if (intensity == 0.0f) {
                stop(device_id);
                return;
            }

            int vibeValue = Math.round(intensity * 255);

            if (vibeValue > 255) {
                vibeValue = 255;
            }
            if (vibeValue < 1) {
                stop(device_id);
                return;
            }
            try {
                haptic.vib.vibrate(VibrationEffect.createOneShot(length, vibeValue));
            }
            catch (Exception e) {
                // Fall back to the generic method, which uses DEFAULT_AMPLITUDE, but works even if
                // something went horribly wrong with the Android 8.0 APIs.
                haptic.vib.vibrate(length);
            }
        }
    }
}