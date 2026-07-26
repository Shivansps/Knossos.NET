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

/* Actual joystick functionality available for API >= 19 devices */
public class SDLJoystickHandler {

    public static class SDLJoystick {
        int device_id;
        String name;
        String desc;
        ArrayList<InputDevice.MotionRange> axes;
        ArrayList<InputDevice.MotionRange> hats;
        ArrayList<Light> lights;
        LightsManager.LightsSession lightsSession;
        SensorManager sensorManager;
        SDLJoySensorListener sensorListener;
        Sensor accelerometerSensor;
        Sensor gyroscopeSensor;
    }
    static class RangeComparator implements Comparator<InputDevice.MotionRange> {
        @Override
        public int compare(InputDevice.MotionRange arg0, InputDevice.MotionRange arg1) {
            // Some controllers, like the Moga Pro 2, return AXIS_GAS (22) for right trigger and AXIS_BRAKE (23) for left trigger - swap them so they're sorted in the right order for SDL
            int arg0Axis = arg0.getAxis();
            int arg1Axis = arg1.getAxis();
            if (arg0Axis == MotionEvent.AXIS_GAS) {
                arg0Axis = MotionEvent.AXIS_BRAKE;
            } else if (arg0Axis == MotionEvent.AXIS_BRAKE) {
                arg0Axis = MotionEvent.AXIS_GAS;
            }
            if (arg1Axis == MotionEvent.AXIS_GAS) {
                arg1Axis = MotionEvent.AXIS_BRAKE;
            } else if (arg1Axis == MotionEvent.AXIS_BRAKE) {
                arg1Axis = MotionEvent.AXIS_GAS;
            }

            // Make sure the AXIS_Z is sorted between AXIS_RY and AXIS_RZ.
            // This is because the usual pairing are:
            // - AXIS_X + AXIS_Y (left stick).
            // - AXIS_RX, AXIS_RY (sometimes the right stick, sometimes triggers).
            // - AXIS_Z, AXIS_RZ (sometimes the right stick, sometimes triggers).
            // This sorts the axes in the above order, which tends to be correct
            // for Xbox-ish game pads that have the right stick on RX/RY and the
            // triggers on Z/RZ.
            //
            // Gamepads that don't have AXIS_Z/AXIS_RZ but use
            // AXIS_LTRIGGER/AXIS_RTRIGGER are unaffected by this.
            //
            // References:
            // - https://developer.android.com/develop/ui/views/touch-and-input/game-controllers/controller-input
            // - https://www.kernel.org/doc/html/latest/input/gamepad.html
            if (arg0Axis == MotionEvent.AXIS_Z) {
                arg0Axis = MotionEvent.AXIS_RZ - 1;
            } else if (arg0Axis > MotionEvent.AXIS_Z && arg0Axis < MotionEvent.AXIS_RZ) {
                --arg0Axis;
            }
            if (arg1Axis == MotionEvent.AXIS_Z) {
                arg1Axis = MotionEvent.AXIS_RZ - 1;
            } else if (arg1Axis > MotionEvent.AXIS_Z && arg1Axis < MotionEvent.AXIS_RZ) {
                --arg1Axis;
            }

            return arg0Axis - arg1Axis;
        }
    }

    private final ArrayList<SDLJoystick> mJoysticks;

    SDLJoystickHandler() {

        mJoysticks = new ArrayList<SDLJoystick>();
    }

    /**
     * Handles adding and removing of input devices.
     */
    synchronized void pollInputDevices() {
        int[] deviceIds = InputDevice.getDeviceIds();

        for (int device_id : deviceIds) {
            if (SDLControllerManager.isDeviceSDLJoystick(device_id)) {
                SDLJoystick joystick = getJoystick(device_id);
                if (joystick == null) {
                    InputDevice joystickDevice = InputDevice.getDevice(device_id);
                    joystick = new SDLJoystick();
                    joystick.device_id = device_id;
                    joystick.name = joystickDevice.getName();
                    joystick.desc = getJoystickDescriptor(joystickDevice);
                    joystick.axes = new ArrayList<InputDevice.MotionRange>();
                    joystick.hats = new ArrayList<InputDevice.MotionRange>();
                    java.util.Set<Integer> axisStrsSet = new java.util.HashSet<Integer>();
                    joystick.lights = new ArrayList<Light>();

                    List<InputDevice.MotionRange> ranges = joystickDevice.getMotionRanges();
                    Collections.sort(ranges, new RangeComparator());
                    for (InputDevice.MotionRange range : ranges) {
                        if (((range.getSource() & InputDevice.SOURCE_CLASS_JOYSTICK) != 0) && axisStrsSet.add(range.getAxis())) {
                            if (range.getAxis() == MotionEvent.AXIS_HAT_X || range.getAxis() == MotionEvent.AXIS_HAT_Y) {
                                joystick.hats.add(range);
                            } else {
                                joystick.axes.add(range);
                            }
                        }
                    }

                    boolean can_rumble = false;
                    boolean has_rgb_led = false;
                    boolean has_accelerometer = false;
                    boolean has_gyroscope = false;
                    if (Build.VERSION.SDK_INT >= 31 /* Android 12.0 (S) */) {
                        VibratorManager vibratorManager = joystickDevice.getVibratorManager();
                        int[] vibrators = vibratorManager.getVibratorIds();
                        if (vibrators.length > 0) {
                            can_rumble = true;
                        }
                        LightsManager lightsManager = joystickDevice.getLightsManager();
                        List<Light> lights = lightsManager.getLights();
                        for (Light light : lights) {
                            if (light.hasRgbControl()) {
                                joystick.lights.add(light);
                            }
                        }
                        if (!joystick.lights.isEmpty()) {
                            joystick.lightsSession = lightsManager.openSession();
                            has_rgb_led = true;
                        }
                        SensorManager sensorManager = joystickDevice.getSensorManager();
                        if (sensorManager != null) {
                            joystick.sensorManager = sensorManager;
                            joystick.sensorListener = new SDLJoySensorListener(joystick.device_id);
                            joystick.accelerometerSensor = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
                            if (joystick.accelerometerSensor != null) {
                                has_accelerometer = true;
                            }
                            joystick.gyroscopeSensor = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE);
                            if (joystick.gyroscopeSensor != null) {
                                has_gyroscope = true;
                            }
                        }
                    }

                    mJoysticks.add(joystick);
                    SDLControllerManager.nativeAddJoystick(joystick.device_id, joystick.name, joystick.desc,
                            getVendorId(joystickDevice), getProductId(joystickDevice),
                            getButtonMask(joystickDevice), joystick.axes.size(), getAxisMask(joystick.axes), joystick.hats.size()/2, can_rumble, has_rgb_led,
                            has_accelerometer, has_gyroscope);
                }
            }
        }

        /* Check removed devices */
        ArrayList<Integer> removedDevices = null;
        for (SDLJoystick joystick : mJoysticks) {
            int device_id = joystick.device_id;
            int i;
            for (i = 0; i < deviceIds.length; i++) {
                if (device_id == deviceIds[i]) break;
            }
            if (i == deviceIds.length) {
                if (removedDevices == null) {
                    removedDevices = new ArrayList<Integer>();
                }
                removedDevices.add(device_id);
            }
        }

        if (removedDevices != null) {
            for (int device_id : removedDevices) {
                SDLControllerManager.nativeRemoveJoystick(device_id);
                for (int i = 0; i < mJoysticks.size(); i++) {
                    if (mJoysticks.get(i).device_id == device_id) {
                        if (Build.VERSION.SDK_INT >= 31 /* Android 12.0 (S) */) {
                            if (mJoysticks.get(i).lightsSession != null) {
                                try {
                                    mJoysticks.get(i).lightsSession.close();
                                } catch (Exception e) {
                                    // Session may already be unregistered when device disconnects
                                }
                                mJoysticks.get(i).lightsSession = null;
                            }
                        }
                        mJoysticks.remove(i);
                        break;
                    }
                }
            }
        }
    }

    synchronized protected SDLJoystick getJoystick(int device_id) {
        for (SDLJoystick joystick : mJoysticks) {
            if (joystick.device_id == device_id) {
                return joystick;
            }
        }
        return null;
    }

    /**
     * Handles given MotionEvent.
     * @param event the event to be handled.
     * @return if given event was processed.
     */
    boolean handleMotionEvent(MotionEvent event) {
        int actionPointerIndex = event.getActionIndex();
        int action = event.getActionMasked();
        if (action == MotionEvent.ACTION_MOVE) {
            SDLJoystick joystick = getJoystick(event.getDeviceId());
            if (joystick != null) {
                for (int i = 0; i < joystick.axes.size(); i++) {
                    InputDevice.MotionRange range = joystick.axes.get(i);
                    /* Normalize the value to -1...1 */
                    float value = (event.getAxisValue(range.getAxis(), actionPointerIndex) - range.getMin()) / range.getRange() * 2.0f - 1.0f;
                    SDLControllerManager.onNativeJoy(joystick.device_id, i, value);
                }
                for (int i = 0; i < joystick.hats.size() / 2; i++) {
                    int hatX = Math.round(event.getAxisValue(joystick.hats.get(2 * i).getAxis(), actionPointerIndex));
                    int hatY = Math.round(event.getAxisValue(joystick.hats.get(2 * i + 1).getAxis(), actionPointerIndex));
                    SDLControllerManager.onNativeHat(joystick.device_id, i, hatX, hatY);
                }
            }
        }
        return true;
    }

    String getJoystickDescriptor(InputDevice joystickDevice) {
        String desc = joystickDevice.getDescriptor();

        if (desc != null && !desc.isEmpty()) {
            return desc;
        }

        return joystickDevice.getName();
    }

    int getProductId(InputDevice joystickDevice) {
        return joystickDevice.getProductId();
    }

    int getVendorId(InputDevice joystickDevice) {
        return joystickDevice.getVendorId();
    }

    int getAxisMask(List<InputDevice.MotionRange> ranges) {
        // For compatibility, keep computing the axis mask like before,
        // only really distinguishing 2, 4 and 6 axes.
        int axis_mask = 0;
        if (ranges.size() >= 2) {
            // ((1 << SDL_GAMEPAD_AXIS_LEFTX) | (1 << SDL_GAMEPAD_AXIS_LEFTY))
            axis_mask |= 0x0003;
        }
        if (ranges.size() >= 4) {
            // ((1 << SDL_GAMEPAD_AXIS_RIGHTX) | (1 << SDL_GAMEPAD_AXIS_RIGHTY))
            axis_mask |= 0x000c;
        }
        if (ranges.size() >= 6) {
            // ((1 << SDL_GAMEPAD_AXIS_LEFT_TRIGGER) | (1 << SDL_GAMEPAD_AXIS_RIGHT_TRIGGER))
            axis_mask |= 0x0030;
        }
        // Also add an indicator bit for whether the sorting order has changed.
        // This serves to disable outdated gamecontrollerdb.txt mappings.
        boolean have_z = false;
        boolean have_past_z_before_rz = false;
        for (InputDevice.MotionRange range : ranges) {
            int axis = range.getAxis();
            if (axis == MotionEvent.AXIS_Z) {
                have_z = true;
            } else if (axis > MotionEvent.AXIS_Z && axis < MotionEvent.AXIS_RZ) {
                have_past_z_before_rz = true;
            }
        }
        if (have_z && have_past_z_before_rz) {
            // If both these exist, the compare() function changed sorting order.
            // Set a bit to indicate this fact.
            axis_mask |= 0x8000;
        }
        return axis_mask;
    }

    int getButtonMask(InputDevice joystickDevice) {
        int button_mask = 0;
        int[] keys = new int[] {
            KeyEvent.KEYCODE_BUTTON_A,
            KeyEvent.KEYCODE_BUTTON_B,
            KeyEvent.KEYCODE_BUTTON_X,
            KeyEvent.KEYCODE_BUTTON_Y,
            KeyEvent.KEYCODE_BACK,
            KeyEvent.KEYCODE_MENU,
            KeyEvent.KEYCODE_BUTTON_MODE,
            KeyEvent.KEYCODE_BUTTON_START,
            KeyEvent.KEYCODE_BUTTON_THUMBL,
            KeyEvent.KEYCODE_BUTTON_THUMBR,
            KeyEvent.KEYCODE_BUTTON_L1,
            KeyEvent.KEYCODE_BUTTON_R1,
            KeyEvent.KEYCODE_DPAD_UP,
            KeyEvent.KEYCODE_DPAD_DOWN,
            KeyEvent.KEYCODE_DPAD_LEFT,
            KeyEvent.KEYCODE_DPAD_RIGHT,
            KeyEvent.KEYCODE_BUTTON_SELECT,
            KeyEvent.KEYCODE_DPAD_CENTER,

            // These don't map into any SDL controller buttons directly
            KeyEvent.KEYCODE_BUTTON_L2,
            KeyEvent.KEYCODE_BUTTON_R2,
            KeyEvent.KEYCODE_BUTTON_C,
            KeyEvent.KEYCODE_BUTTON_Z,
            KeyEvent.KEYCODE_BUTTON_1,
            KeyEvent.KEYCODE_BUTTON_2,
            KeyEvent.KEYCODE_BUTTON_3,
            KeyEvent.KEYCODE_BUTTON_4,
            KeyEvent.KEYCODE_BUTTON_5,
            KeyEvent.KEYCODE_BUTTON_6,
            KeyEvent.KEYCODE_BUTTON_7,
            KeyEvent.KEYCODE_BUTTON_8,
            KeyEvent.KEYCODE_BUTTON_9,
            KeyEvent.KEYCODE_BUTTON_10,
            KeyEvent.KEYCODE_BUTTON_11,
            KeyEvent.KEYCODE_BUTTON_12,
            KeyEvent.KEYCODE_BUTTON_13,
            KeyEvent.KEYCODE_BUTTON_14,
            KeyEvent.KEYCODE_BUTTON_15,
            KeyEvent.KEYCODE_BUTTON_16,
        };
        int[] masks = new int[] {
            (1 << 0),   // A -> A
            (1 << 1),   // B -> B
            (1 << 2),   // X -> X
            (1 << 3),   // Y -> Y
            (1 << 4),   // BACK -> BACK
            (1 << 6),   // MENU -> START
            (1 << 5),   // MODE -> GUIDE
            (1 << 6),   // START -> START
            (1 << 7),   // THUMBL -> LEFTSTICK
            (1 << 8),   // THUMBR -> RIGHTSTICK
            (1 << 9),   // L1 -> LEFTSHOULDER
            (1 << 10),  // R1 -> RIGHTSHOULDER
            (1 << 11),  // DPAD_UP -> DPAD_UP
            (1 << 12),  // DPAD_DOWN -> DPAD_DOWN
            (1 << 13),  // DPAD_LEFT -> DPAD_LEFT
            (1 << 14),  // DPAD_RIGHT -> DPAD_RIGHT
            (1 << 4),   // SELECT -> BACK
            (1 << 0),   // DPAD_CENTER -> A
            (1 << 15),  // L2 -> ??
            (1 << 16),  // R2 -> ??
            (1 << 17),  // C -> ??
            (1 << 18),  // Z -> ??
            (1 << 20),  // 1 -> ??
            (1 << 21),  // 2 -> ??
            (1 << 22),  // 3 -> ??
            (1 << 23),  // 4 -> ??
            (1 << 24),  // 5 -> ??
            (1 << 25),  // 6 -> ??
            (1 << 26),  // 7 -> ??
            (1 << 27),  // 8 -> ??
            (1 << 28),  // 9 -> ??
            (1 << 29),  // 10 -> ??
            (1 << 30),  // 11 -> ??
            (1 << 31),  // 12 -> ??
            // We're out of room...
            0xFFFFFFFF,  // 13 -> ??
            0xFFFFFFFF,  // 14 -> ??
            0xFFFFFFFF,  // 15 -> ??
            0xFFFFFFFF,  // 16 -> ??
        };
        boolean[] has_keys = joystickDevice.hasKeys(keys);
        for (int i = 0; i < keys.length; ++i) {
            if (has_keys[i]) {
                button_mask |= masks[i];
            }
        }
        return button_mask;
    }

    void setLED(int device_id, int red, int green, int blue) {
        if (Build.VERSION.SDK_INT < 31 /* Android 12.0 (S) */) {
            return;
        }
        SDLJoystick joystick = getJoystick(device_id);
        if (joystick == null || joystick.lights.isEmpty()) {
            return;
        }
        LightsRequest.Builder lightsRequest = new LightsRequest.Builder();
        LightState lightState = new LightState.Builder().setColor(Color.rgb(red, green, blue)).build();
        for (Light light : joystick.lights) {
            if (light.hasRgbControl()) {
                lightsRequest.addLight(light, lightState);
            }
        }
        joystick.lightsSession.requestLights(lightsRequest.build());
    }

    void setSensorsEnabled(int device_id, boolean enabled) {
        if (Build.VERSION.SDK_INT < 31 /* Android 12.0 (S) */) {
            return;
        }
        SDLJoystick joystick = getJoystick(device_id);
        if (joystick == null || joystick.sensorManager == null) {
            return;
        }
        if (enabled) {
            if (joystick.accelerometerSensor != null) {
                SDLSensorManager.registerListener(joystick.sensorManager, joystick.sensorListener, joystick.accelerometerSensor, SensorManager.SENSOR_DELAY_GAME);
            }
            if (joystick.gyroscopeSensor != null) {
                SDLSensorManager.registerListener(joystick.sensorManager, joystick.sensorListener, joystick.gyroscopeSensor, SensorManager.SENSOR_DELAY_GAME);
            }
        } else {
            if (joystick.accelerometerSensor != null) {
                SDLSensorManager.unregisterListener(joystick.sensorManager, joystick.sensorListener, joystick.accelerometerSensor);
            }
            if (joystick.gyroscopeSensor != null) {
                SDLSensorManager.unregisterListener(joystick.sensorManager, joystick.sensorListener, joystick.gyroscopeSensor);
            }
        }
    }
}