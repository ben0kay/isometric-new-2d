# Run after building C#: godot --headless --path . --script res://Tests/PlayerDirectionalRigChecks.gd
extends SceneTree
func _initialize():
 var rig=load("res://PLAYER/Artwork/PlayerDirectionalRig.tscn").instantiate()
 root.add_child.call_deferred(rig)
 await process_frame
 assert(rig.get_node("Front").visible and not rig.get_node("Rear").visible)
 rig.call("UpdatePresentation",Vector2(200,-100),false,false)
 assert(rig.get_node("Rear").visible and not rig.get_node("Front").visible)
 assert(rig.get_node("Rear/AnimationPlayer").active and not rig.get_node("Front/AnimationPlayer").active)
 assert(rig.get_node("Rear/AnimationPlayer").current_animation=="walk")
 rig.call("UpdatePresentation",Vector2(-200,-100),false,true)
 assert(rig.get_node("Rear/AnimationPlayer").current_animation=="run")
 rig.call("UpdatePresentation",Vector2.ZERO,false,false)
 assert(rig.get_node("Rear").visible and rig.get_node("Rear/AnimationPlayer").current_animation=="idle")
 rig.call("UpdatePresentation",Vector2.ZERO,true,false)
 assert(rig.get_node("Rear/AnimationPlayer").current_animation=="jump")
 rig.call("UpdatePresentation",Vector2(200,100),false,false)
 assert(rig.get_node("Front").visible and rig.get_node("Front/AnimationPlayer").current_animation=="walk")
 # Direction changes preserve cycle phase and use a matching neutral silhouette.
 rig.get_node("Front/AnimationPlayer").advance(0.11)
 var phase=rig.get_node("Front/AnimationPlayer").current_animation_position
 rig.call("UpdatePresentation",Vector2(200,-100),false,false)
 assert(abs(rig.get_node("Rear/AnimationPlayer").current_animation_position-phase)<0.001)
 assert(rig.get_node("ShadowSource").texture.resource_path.ends_with("Player01Rear.png"))
 assert(rig.get_node("Front").find_children("ShadowSource","Sprite2D",true,false).size()==0)
 assert(rig.get_node("Rear").find_children("ShadowSource","Sprite2D",true,false).size()==0)
 for branch in ["Front","Rear"]:
  var view=rig.get_node(branch)
  var ap=view.get_node("AnimationPlayer")
  ap.active=true
  for name in ["RESET","idle","walk","run","jump","dodge_preview"]:
   var a=ap.get_animation(name)
   for i in range(a.get_track_count()):
    var path=str(a.track_get_path(i))
    assert(view.has_node(path.split(":")[0]),branch+" missing animation target "+path)
    if path.ends_with(":rotation"):
     for k in range(a.track_get_key_count(i)):
      assert(typeof(a.track_get_key_value(i,k))==TYPE_FLOAT)
  for name in ["walk","run"]:
   ap.play(name,0.08)
   ap.advance(0.09)
   ap.advance(0.02)
   var thigh=view.get_node("Motion/Pelvis/FrontThigh")
   var before=thigh.rotation
   assert(abs(before)>0.01,branch+" "+name+" thigh stuck")
   ap.advance(ap.get_animation(name).length*0.5)
   assert(abs(thigh.rotation-before)>0.05)
  for part in view.find_children("*","Polygon2D",true,false):
   assert(part.z_index==0,"Part overrides world Y sorting")
  print("PASS: ",branch," valid float tracks and animated walk/run limbs")
 var near=rig.get_node("Rear/Motion/Pelvis/Torso/FrontUpperArm")
 var before_x=near.global_position.x
 rig.scale.x=-1
 assert(abs(near.global_position.x+before_x)<0.01,"Rear limbs do not mirror together")
 print("PASS: rear direction selection, idle retention, jump, phase continuity, mirror and single shadow")
 quit()
