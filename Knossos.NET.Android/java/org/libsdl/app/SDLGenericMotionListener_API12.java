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

public class SDLGenericMotionListener_API12 implements View.OnGenericMotionListener {
    // Generic Motion (mouse hover, joystick...) events go here
    @Override
    public boolean onGenericMotion(View v, MotionEvent event) {
        float x, y;
        int action;

        switch ( event.getSource() ) {
            case InputDevice.SOURCE_JOYSTICK:
                return SDLControllerManager.handleJoystickMotionEvent(event);

            case InputDevice.SOURCE_MOUSE:
                action = event.getActionMasked();
                switch (action) {
                    case MotionEvent.ACTION_SCROLL:
                        x = event.getAxisValue(MotionEvent.AXIS_HSCROLL, 0);
                        y = event.getAxisValue(MotionEvent.AXIS_VSCROLL, 0);
                        SDLActivity.onNativeMouse(0, action, x, y, false);
                        return true;

                    case MotionEvent.ACTION_HOVER_MOVE:
                        x = event.getX(0);
                        y = event.getY(0);

                        SDLActivity.onNativeMouse(0, action, x, y, false);
                        return true;

                    default:
                        break;
                }
                break;

            default:
                break;
        }

        // Event was not managed
        return false;
    }

    public boolean supportsRelativeMouse() {
        return false;
    }

    public boolean inRelativeMode() {
        return false;
    }

    public boolean setRelativeMouseEnabled(boolean enabled) {
        return false;
    }

    public void reclaimRelativeMouseModeIfNeeded()
    {

    }

    public float getEventX(MotionEvent event) {
        return event.getX(0);
    }

    public float getEventY(MotionEvent event) {
        return event.getY(0);
    }

}