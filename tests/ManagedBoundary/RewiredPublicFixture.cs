using System;
using System.Collections.Generic;
using System.Text;
namespace UnityEngine
{
    public struct Keyframe
    {public float time {get;set;}public float value {get;set;}public float inTangent {get;set;}public float outTangent {get;set;}public int tangentMode {get;set;}public float inWeight {get;set;}public float outWeight {get;set;}public int weightedMode {get;set;}}
    public class AnimationCurve
    {public int preWrapMode {get;set;}public int postWrapMode {get;set;}public Keyframe[] keys {get;set;}=new[]{new Keyframe {time=-1,value=-1},new Keyframe {time=1,value=1}};public int length {get {return keys.Length;}}}
}
namespace Rewired
{
    public class ControllerElementIdentifier
    {public int id {get;set;}public string name {get;set;}public string positiveName {get;set;}public string negativeName {get;set;}public ControllerElementType elementType {get;set;}public int compoundElementType {get;set;}}
    public enum ControllerType {Joystick=2,Custom=20}
    public enum ControllerElementType {Axis=0,Button=1}
    public enum Pole {Positive=0,Negative=1}
    public enum AxisRange {Full=0,Positive=1,Negative=2}
    public class Controller
    {
        public readonly int id;public ControllerType type;public string tag {get;set;}public bool enabled=true;public Controller(int id) {this.id=id;}
        public class Axis {public int id;public string name;public ControllerElementType type;public float pollingDeadZone;public ControllerElementIdentifier elementIdentifier {get;set;}}
    }
    public class ControllerWithAxes : Controller
    {
        public ControllerWithAxes(int id):base(id) {}
        public List<Axis> Axes {get;set;}=new List<Axis>();public List<ControllerElementIdentifier> AxisElementIdentifiers {get;set;}=new List<ControllerElementIdentifier>();
        public int axisCount {get {return Axes.Count;}}public CalibrationMap calibrationMap {get;set;}=new CalibrationMap();
        public int GetAxisIndexById(int id) {return Axes.FindIndex(a=>a.id==id);}
    }
    public class AxisCalibration
    {public bool enabled {get;set;}=true;public float deadZone {get;set;}public float calibratedZero {get;set;}public float calibratedMin {get;set;}=-1;public float calibratedMax {get;set;}=1;public bool invert {get;set;}public int sensitivityType {get;set;}public float sensitivity {get;set;}=1;public bool applyRangeCalibration {get;set;}public UnityEngine.AnimationCurve sensitivityCurve {get;set;}=new UnityEngine.AnimationCurve();}
    public sealed class CalibrationMap
    {public bool EmptyXml;public List<AxisCalibration> Axes {get;set;}=new List<AxisCalibration>{new AxisCalibration(),new AxisCalibration(),new AxisCalibration()};public int axisCount {get {return Axes.Count;}}public string ToXmlString() {return EmptyXml?"":"controlled-calibration";}}
    public sealed class CustomController : ControllerWithAxes
    {
        public int sourceControllerId;
        public float[] Values=new float[3];
        public CustomController(int id,int source):base(id) {sourceControllerId=source;type=ControllerType.Custom;for(int i=0;i<3;++i){var identifier=new ControllerElementIdentifier {id=i,name="Axis"+i,positiveName="Axis"+i+"+",negativeName="Axis"+i+"-"};AxisElementIdentifiers.Add(identifier);Axes.Add(new Axis {id=i,name=identifier.name,elementIdentifier=identifier});}}
        public void SetAxisValue(int index,float value) {Values[index]=value;}
    }
    public class ActionElementMap
    {
        public int actionId {get;set;}
        public int elementIdentifierId {get;set;}
        public ControllerElementType elementType {get;set;}
        public AxisRange axisRange {get;set;}
        public Pole axisContribution {get;set;}
        public bool invert {get;set;}
        public int keyboardKeyCode {get;set;}
        public int modifierKey1 {get;set;}
        public int modifierKey2 {get;set;}
        public int modifierKey3 {get;set;}
        public bool enabled {get;set;}
        public ActionElementMap(int action,ControllerElementType type,int id,Pole pole,AxisRange range,bool invert)
        {actionId=action;elementType=type;elementIdentifierId=id;axisContribution=pole;axisRange=range;this.invert=invert;enabled=true;}
    }
    public class ControllerMap
    {
        public ControllerType controllerType=ControllerType.Custom;
        public int controllerId;
        public List<ActionElementMap> Entries=new List<ActionElementMap>();
        public bool EmptyXml;
        public IEnumerable<ActionElementMap> AllMaps {get {return Entries;}}
        public string ToXmlString() {if(EmptyXml)return "";var text=new StringBuilder();foreach(var entry in Entries) text.Append(entry.actionId).Append('/').Append(entry.elementIdentifierId).Append('/').Append(entry.invert).Append(';');return text.ToString();}
    }
    public class Player
    {
        public readonly ControllerHelper controllers;
        public Player() {controllers=new ControllerHelper(this);}
        public class ControllerHelper
        {
            readonly Player owner;
            public readonly MapHelper maps;
            public List<Controller> Controllers {get;private set;}
            public ControllerHelper(Player owner) {this.owner=owner;maps=new MapHelper(owner);Controllers=new List<Controller>();}
            public void AddController(Controller controller,bool removeFromOtherPlayers)
            {if(removeFromOtherPlayers) throw new Exception("Unexpected unassignment policy");Controllers.Add(controller);}
            public class MapHelper
            {
                public List<ControllerMap> Content=new List<ControllerMap>();
                readonly Player owner;
                public MapHelper(Player owner) {this.owner=owner;}
                public void LoadMap(ControllerType type,int id,int category,int layout)
                {
                    CustomController controller=ReInput.controllers.CustomControllers.Find(c=>c.id==id);
                    if(controller==null || !owner.controllers.Controllers.Contains(controller)) throw new Exception("No assignment");
                    var editor=ReInput.Data.GetCustomControllerMap(category,controller.sourceControllerId,layout);
                    if(editor==null) throw new Exception("No editor map");
                    var map=new ControllerMap {controllerId=id};
                    foreach(var e in editor.actionElementMaps) map.Entries.Add(new ActionElementMap(e.actionId,e.elementType,e.elementIdentifierId,e.axisContribution,e.axisRange,e.invert));
                    Content.Add(map);
                }
                public IEnumerable<ControllerMap> GetAllMaps() {return Content;}
            }
        }
    }
    public class InputManager_Base {public Data.UserData userData {get {return ReInput.Data;}}}
    public static class ReInput
    {
        public static Data.UserData Data;
        public static bool isReady {get;set;}
        public static ControllerHelper controllers {get;private set;}
        public static PlayerHelper players {get;private set;}
        public static event Action InputSourceUpdateEvent;
        public static void Reset() {Data=new Data.UserData();controllers=new ControllerHelper();players=new PlayerHelper();isReady=true;InputSourceUpdateEvent=null;}
        public static void Pulse() {if(InputSourceUpdateEvent!=null) InputSourceUpdateEvent();}
        public static int Subscribers {get {return InputSourceUpdateEvent==null?0:InputSourceUpdateEvent.GetInvocationList().Length;}}
        public class PlayerHelper
        {
            public Player SystemPlayer {get;private set;}
            public IList<Player> AllPlayers {get;private set;}
            public PlayerHelper() {SystemPlayer=new Player();AllPlayers=new List<Player>{new Player()};}
        }
        public class ControllerHelper
        {
            public List<CustomController> CustomControllers {get;private set;}
            public List<Controller> Joysticks {get;private set;}
            public bool ThrowCreateAfter,ForeignCreateNull,ForeignCreateThrow;
            public int Destroys;
            public Action DestroyAction;
            public ControllerHelper() {CustomControllers=new List<CustomController>();Joysticks=new List<Controller>();}
            public CustomController CreateCustomController(int definitionId,string tag)
            {if(Data.GetCustomControllerById(definitionId)==null)return null;var controller=new CustomController(40,definitionId){tag=ForeignCreateNull||ForeignCreateThrow?"game-owned":tag};CustomControllers.Add(controller);if(ThrowCreateAfter||ForeignCreateThrow)throw new Exception("Creation after mutation");return ForeignCreateNull?null:controller;}
            public bool DestroyCustomController(CustomController controller)
            {
                ++Destroys;var all=new List<Player>(players.AllPlayers);all.Add(players.SystemPlayer);
                foreach(var player in all) {player.controllers.Controllers.Remove(controller);player.controllers.maps.Content.RemoveAll(m=>m.controllerId==controller.id && m.controllerType==ControllerType.Custom);}
                CustomControllers.Remove(controller);if(DestroyAction!=null)DestroyAction();return true;
            }
        }
    }
}
namespace Rewired.Data
{
    public class CustomController_Editor
    {
        public int id {get;set;}
        public string name {get;set;}="CustomController";
        public string descriptiveName {get;set;}="CustomController";
        public Guid typeGuid {get;set;}=Guid.NewGuid();
        public int axisCount {get {return axes.Count;}}
        public int buttonCount {get {return buttons.Count;}}
        public List<Axis> axes {get;private set;}=new List<Axis>();
        public List<object> buttons {get;private set;}=new List<object>();
        public List<Rewired.ControllerElementIdentifier> elementIdentifiers {get;private set;}=new List<Rewired.ControllerElementIdentifier>();
        public bool ThrowAddAxisBefore;
        public class Axis
        {
            public int elementIdentifierId;public string name;public Rewired.AxisRange range;public bool invert,doNotCalibrateRange;public float deadZone,zero,min=-1,max=1,sensitivity=1;public int sensitivityType;
            public UnityEngine.AnimationCurve sensitivityCurve=new UnityEngine.AnimationCurve();public Mapping.HardwareAxisInfo axisInfo=new Mapping.HardwareAxisInfo();
            public int Id {get {return elementIdentifierId;}set {elementIdentifierId=value;}}public string Name {get {return name;}set {name=value;}}
        }
        public void AddAxis() {if(ThrowAddAxisBefore)throw new Exception("Axis mutation refused");var axis=new Axis {Id=axes.Count,Name="Axis"+axes.Count};axes.Add(axis);elementIdentifiers.Add(new Rewired.ControllerElementIdentifier {id=axis.Id,name=axis.Name,positiveName=axis.Name+"+",negativeName=axis.Name+"-"});}
        public int[] GetAxisElementIdentifierIds() {return axes.ConvertAll(a=>a.Id).ToArray();}
        public string[] GetAxisNames() {return axes.ConvertAll(a=>a.Name).ToArray();}
    }
    public class UserData
    {
        public List<CustomController_Editor> Definitions=new List<CustomController_Editor>();
        public List<Mapping.ControllerMap_Editor> Maps=new List<Mapping.ControllerMap_Editor>();
        public int Deletes;
        public bool BadAction,ThrowAxisBefore,AddEntryDuringMapCreate,ThrowDefinitionAfter;
        public void AddCustomController() {Definitions.Add(new CustomController_Editor {id=2,ThrowAddAxisBefore=ThrowAxisBefore});if(ThrowDefinitionAfter)throw new Exception("Unknown definition creation outcome");}
        public int[] GetCustomControllerIds() {return Definitions.ConvertAll(d=>d.id).ToArray();}
        public CustomController_Editor GetCustomControllerById(int id) {return Definitions.Find(d=>d.id==id);}
        public int GetCustomControllerIndex(int id) {return Definitions.FindIndex(d=>d.id==id);}
        public void DeleteCustomController(int index) {++Deletes;int id=Definitions[index].id;Maps.RemoveAll(m=>m.customControllerUid==id);Definitions.RemoveAt(index);}
        public string GetActionNameById(int id) {return BadAction?"changed":id==31?"LookXAxis_Stick":id==32?"LookYAxis_Stick":id==36?"DialogDownButton":"other";}
        public int[] GetMapCategoryIds() {return new[]{0};}
        public int[] GetCustomControllerLayoutIds() {return new[]{0};}
        public bool CreateCustomControllerMap(int categoryId,int controllerUid,int layoutId)
        {if(categoryId!=0 || layoutId!=0 || GetCustomControllerById(controllerUid)==null)throw new Exception("Incorrect map identity arguments");var map=new Mapping.ControllerMap_Editor {id=8,categoryId=categoryId,layoutId=layoutId,customControllerUid=controllerUid};Maps.Add(map);if(AddEntryDuringMapCreate)map.actionElementMaps.Add(new Rewired.ActionElementMap(99,Rewired.ControllerElementType.Axis,9,Rewired.Pole.Positive,Rewired.AxisRange.Full,false));return true;}
        public Mapping.ControllerMap_Editor GetCustomControllerMap(int category,int uid,int layout) {return Maps.Find(m=>m.categoryId==category && m.customControllerUid==uid && m.layoutId==layout);}
        public int GetCustomControllerMapCount(int id) {return Maps.FindAll(m=>m.customControllerUid==id).Count;}
    }
}
namespace Rewired.Data.Mapping
{
    public class HardwareAxisInfo
    {public int dataFormat {get;set;}public bool excludeFromPolling {get;set;}public int specialAxisType {get;set;}public float pollingDeadZone {get;set;}}
    public class ControllerMap_Editor
    {
        public int id,categoryId,layoutId,customControllerUid;
        public string name="",hardwareGuidString="";
        public List<Rewired.ActionElementMap> actionElementMaps=new List<Rewired.ActionElementMap>();
    }
}
