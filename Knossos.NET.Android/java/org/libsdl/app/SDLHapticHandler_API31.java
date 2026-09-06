package org.libsdl.app;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

import android.content.Context;
import android.hardware.lights.Light;
import android.hardware.lights.LightsRequest;
import android.hardware.lights.LightsManager;
import android.hardware.lights.LightState;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.graphics.Color;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.os.VibratorManager;
import android.util.Log;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
public class SDLHapticHandler_API31 extends SDLHapticHandler {
    @Override
    void run(int device_id, float intensity, int length) {
        SDLHaptic haptic = getHaptic(device_id);
        if (haptic != null) {
            vibrate(haptic.vib, intensity, length);
        }
    }

    @Override
    void rumble(int device_id, float low_frequency_intensity, float high_frequency_intensity, int length) {
        InputDevice device = InputDevice.getDevice(device_id);
        if (device == null) {
            return;
        }

        if (Build.VERSION.SDK_INT < 31 /* Android 12.0 (S) */) {
            /* Silence 'lint' warning */
            return;
        }

        VibratorManager manager = device.getVibratorManager();
        int[] vibrators = manager.getVibratorIds();
        if (vibrators.length >= 2) {
            vibrate(manager.getVibrator(vibrators[0]), low_frequency_intensity, length);
            vibrate(manager.getVibrator(vibrators[1]), high_frequency_intensity, length);
        } else if (vibrators.length == 1) {
            float intensity = (low_frequency_intensity * 0.6f) + (high_frequency_intensity * 0.4f);
            vibrate(manager.getVibrator(vibrators[0]), intensity, length);
        }
    }

    private void vibrate(Vibrator vibrator, float intensity, int length) {

        if (Build.VERSION.SDK_INT < 31 /* Android 12.0 (S) */) {
            /* Silence 'lint' warning */
            return;
        }

        if (intensity == 0.0f) {
            vibrator.cancel();
            return;
        }

        int value = Math.round(intensity * 255);
        if (value > 255) {
            value = 255;
        }
        if (value < 1) {
            vibrator.cancel();
            return;
        }
        try {
            vibrator.vibrate(VibrationEffect.createOneShot(length, value));
        }
        catch (Exception e) {
            // Fall back to the generic method, which uses DEFAULT_AMPLITUDE, but works even if
            // something went horribly wrong with the Android 8.0 APIs.
            vibrator.vibrate(length);
        }
    }
}