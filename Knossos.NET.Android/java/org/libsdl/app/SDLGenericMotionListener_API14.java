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

public class SDLGenericMotionListener_API14 implements View.OnGenericMotionListener {
    protected static final int SDL_PEN_DEVICE_TYPE_UNKNOWN = 0;
    protected static final int SDL_PEN_DEVICE_TYPE_DIRECT = 1;
    protected static final int SDL_PEN_DEVICE_TYPE_INDIRECT = 2;

    // Generic Motion (mouse hover, joystick...) events go here
    @Override
    public boolean onGenericMotion(View v, MotionEvent event) {
        if (event.getSource() == InputDevice.SOURCE_JOYSTICK)
            return SDLControllerManager.handleJoystickMotionEvent(event);

        float x, y;
        int action = event.getActionMasked();
        int pointerCount = event.getPointerCount();
        boolean consumed = false;

        for (int i = 0; i < pointerCount; i++) {
            int toolType = event.getToolType(i);

            if (toolType == MotionEvent.TOOL_TYPE_MOUSE) {
                switch (action) {
                    case MotionEvent.ACTION_SCROLL:
                        x = event.getAxisValue(MotionEvent.AXIS_HSCROLL, i);
                        y = event.getAxisValue(MotionEvent.AXIS_VSCROLL, i);
                        SDLActivity.onNativeMouse(0, action, x, y, false);
                        consumed = true;
                        break;

                    case MotionEvent.ACTION_HOVER_MOVE:
                        x = getEventX(event, i);
                        y = getEventY(event, i);

                        SDLActivity.onNativeMouse(0, action, x, y, checkRelativeEvent(event));
                        consumed = true;
                        break;

                    default:
                        break;
                }
            } else if (toolType == MotionEvent.TOOL_TYPE_STYLUS || toolType == MotionEvent.TOOL_TYPE_ERASER) {
                switch (action) {
                    case MotionEvent.ACTION_HOVER_ENTER:
                    case MotionEvent.ACTION_HOVER_MOVE:
                    case MotionEvent.ACTION_HOVER_EXIT:
                        x = event.getX(i);
                        y = event.getY(i);
                        float p = event.getPressure(i);
                        if (p > 1.0f) {
                            // may be larger than 1.0f on some devices
                            // see the documentation of getPressure(i)
                            p = 1.0f;
                        }

                        // BUTTON_STYLUS_PRIMARY is 2^5, so shift by 4, and apply SDL_PEN_INPUT_DOWN/SDL_PEN_INPUT_ERASER_TIP
                        int buttons = (event.getButtonState() >> 4) | (1 << (toolType == MotionEvent.TOOL_TYPE_STYLUS ? 0 : 30));
                        if ((event.getButtonState() & MotionEvent.BUTTON_TERTIARY) != 0) {
                            buttons |= 0x08;
                        }

                        SDLActivity.onNativePen(event.getPointerId(i), getPenDeviceType(event.getDevice()), buttons, action, x, y, p);
                        consumed = true;
                        break;
                }
            }
        }

        return consumed;
    }

    boolean supportsRelativeMouse() {
        return false;
    }

    boolean inRelativeMode() {
        return false;
    }

    boolean setRelativeMouseEnabled(boolean enabled) {
        return false;
    }

    void reclaimRelativeMouseModeIfNeeded() {

    }

    boolean checkRelativeEvent(MotionEvent event) {
        return inRelativeMode();
    }

    float getEventX(MotionEvent event, int pointerIndex) {
        return event.getX(pointerIndex);
    }

    float getEventY(MotionEvent event, int pointerIndex) {
        return event.getY(pointerIndex);
    }

    int getPenDeviceType(InputDevice penDevice) {
        return SDL_PEN_DEVICE_TYPE_UNKNOWN;
    }
}