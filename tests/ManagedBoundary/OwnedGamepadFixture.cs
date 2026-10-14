using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
namespace UnityEngine.InputSystem.Controls
{
    public class ButtonControl {public bool isPressed {get;set;}}
    public class StickControl {public Vector2 Value;public Vector2 ReadUnprocessedValue() {return Value;}}
}
namespace UnityEngine.InputSystem.Layouts
{
    public struct InputDeviceMatcher {}
    public struct InputDeviceDescription
    {
        public string interfaceName {get;set;} public string manufacturer {get;set;} public string product {get;set;} public string serial {get;set;} public string capabilities {get;set;}
    }
    public class InputControlLayout {public string Json;public string ToJson() {return Json;}}
}
namespace UnityEngine.InputSystem.LowLevel
{
    public enum GamepadButton {South=6,West=11}
    public struct GamepadState {public uint buttons;public Vector2 leftStick;public Vector2 rightStick;}
}
namespace UnityEngine.InputSystem
{
    public enum InputDeviceChange {Added,Removed}
    public enum InputControlLayoutChange {Added,Replaced,Removed}
    public class InputDevice
    {
        public bool added {get;set;} public bool enabled {get;set;} public string layout {get;set;} public Layouts.InputDeviceDescription description {get;set;}
    }
    public class Gamepad : InputDevice
    {
        public static Gamepad current {get;set;}
        public Controls.ButtonControl buttonSouth {get;private set;}=new Controls.ButtonControl();
        public Controls.ButtonControl buttonWest {get;private set;}=new Controls.ButtonControl();
        public Controls.StickControl leftStick {get;private set;}=new Controls.StickControl();
        public Controls.StickControl rightStick {get;private set;}=new Controls.StickControl();
        public void MakeCurrent() {current=this;}
    }
    public static class InputSystem
    {
        public static event Action<InputDevice,InputDeviceChange> onDeviceChange;
        public static event Action<string,InputControlLayoutChange> onLayoutChange;
        public static readonly Dictionary<string,Layouts.InputControlLayout> Layouts=new Dictionary<string,Layouts.InputControlLayout>();
        public static readonly List<InputDevice> Devices=new List<InputDevice>();
        public static InputDevice[] devices {get {return Devices.ToArray();}}
        public static readonly List<Tuple<InputDevice,LowLevel.GamepadState>> Pending=new List<Tuple<InputDevice,LowLevel.GamepadState>>();
        public static int Registers,Adds,Removes,LayoutRemoves,Queues;
        public static bool AddThenThrow,RemoveThenThrow,RemoveAlwaysThrow,QueueThenThrow,DisableAdded,Collision,RegisterThenThrow;
        public static Gamepad CurrentOnRemoval;
        public static InputDevice Keyboard;
        public static int DeviceObservers {get {return onDeviceChange==null?0:onDeviceChange.GetInvocationList().Length;}}
        public static int LayoutObservers {get {return onLayoutChange==null?0:onLayoutChange.GetInvocationList().Length;}}
        public static void Reset()
        {
            Layouts.Clear();Devices.Clear();Pending.Clear();Gamepad.current=null;onDeviceChange=null;onLayoutChange=null;
            Registers=Adds=Removes=LayoutRemoves=Queues=0;AddThenThrow=RemoveThenThrow=RemoveAlwaysThrow=QueueThenThrow=DisableAdded=Collision=RegisterThenThrow=false;CurrentOnRemoval=null;
            Keyboard=new InputDevice {added=true,enabled=true,layout="Keyboard"};Devices.Add(Keyboard);
        }
        public static void RegisterLayout(string json,string name,Layouts.InputDeviceMatcher? matches)
        {
            ++Registers;Layouts[name]=new Layouts.InputControlLayout {Json=json};onLayoutChange?.Invoke(name,InputControlLayoutChange.Added);
            if(RegisterThenThrow) throw new InvalidOperationException("Controlled registration fault after effect.");
        }
        public static Layouts.InputControlLayout LoadLayout(string name)
        {
            if(Collision) return new Layouts.InputControlLayout {Json="external"};return Layouts.TryGetValue(name,out var value)?value:null;
        }
        public static InputDevice AddDevice(Layouts.InputDeviceDescription description)
        {
            ++Adds;string found=null;
            foreach(var pair in Layouts)
            {
                using var json=JsonDocument.Parse(pair.Value.Json);
                if(json.RootElement.GetProperty("device").GetProperty("interface").GetString()==description.interfaceName) found=pair.Key;
            }
            if(found==null) throw new InvalidOperationException("No exact synthetic matcher.");
            var d=new Gamepad {added=true,enabled=!DisableAdded,layout=found,description=description};Devices.Add(d);d.MakeCurrent();onDeviceChange?.Invoke(d,InputDeviceChange.Added);
            if(AddThenThrow) throw new InvalidOperationException("Controlled add fault after effect.");return d;
        }
        public static void QueueStateEvent<TState>(InputDevice device,TState state,double time) where TState:struct
        {
            if(!device.added || !(device is Gamepad) || time!=-1) throw new InvalidOperationException("Wrong queue target.");
            ++Queues;Pending.Add(Tuple.Create(device,(LowLevel.GamepadState)(object)state));if(QueueThenThrow) throw new InvalidOperationException("Controlled queued-state fault after effect.");
        }
        public static void NormalUpdate()
        {
            foreach(var entry in Pending) if(entry.Item1.added && entry.Item1.enabled)
            {
                var d=(Gamepad)entry.Item1;d.buttonSouth.isPressed=(entry.Item2.buttons&(1u<<6))!=0;d.buttonWest.isPressed=(entry.Item2.buttons&(1u<<11))!=0;d.leftStick.Value=entry.Item2.leftStick;d.rightStick.Value=entry.Item2.rightStick;d.MakeCurrent();
            }
            Pending.Clear();
        }
        public static void RemoveDevice(InputDevice device)
        {
            ++Removes;if(RemoveAlwaysThrow) throw new InvalidOperationException("Controlled removal unavailable.");
            device.added=false;Devices.Remove(device);if(System.Object.ReferenceEquals(Gamepad.current,device)) Gamepad.current=null;
            onDeviceChange?.Invoke(device,InputDeviceChange.Removed);if(CurrentOnRemoval!=null) CurrentOnRemoval.MakeCurrent();
            if(RemoveThenThrow) throw new InvalidOperationException("Controlled removal throw after effect.");
        }
        public static void RemoveLayout(string name) {++LayoutRemoves;Layouts.Remove(name);onLayoutChange?.Invoke(name,InputControlLayoutChange.Removed);}
        public static void Replace(string name) {Layouts[name].Json="external replacement";onLayoutChange?.Invoke(name,InputControlLayoutChange.Replaced);}
    }
}
