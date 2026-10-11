// Exercises interaction selection, dispatch, modal priority and release suppression.
using Godot;
using System;
using System.Reflection;
public partial class InteractionChecks : Node
{
 private class Provider : IInteractionProvider
 {
  public bool Eligible=true;public int Calls;
  public string DisplayName=>"Test wreck";
  public InteractionAction PrimaryAction=>new("open","Interact");
  public bool CanInteract(Player p)=>Eligible;
  public bool Execute(Player p,string id){if(!Eligible||id!="open")return false;Calls++;return true;}
 }
 public partial class TestPlayer : Player
 { public override void _Ready(){} public override void _PhysicsProcess(double d){} }
 public partial class TestStorage : StorageHud
 { public override void _Ready(){} public override void _Process(double d){} public override void _ExitTree(){} }
 public partial class TestWorldStorage : WorldStorage { public override void _Ready(){} }
 static void Set(object o,string f,object v)=>o.GetType().GetField(f,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static void Check(bool c,string m){if(!c)throw new Exception(m);}
 public override async void _Ready()
 {
  try {
   var modes=new InputModes();AddChild(modes);
   var player=new TestPlayer();AddChild(player);var systems=new Node{Name="Systems"};player.AddChild(systems);var health=new Health{Name="Health"};systems.AddChild(health);
   var storage=new TestStorage{Name="StorageHud"};player.AddChild(storage);
   PlayerInteractionController.Attach(player,storage);
   var controller=player.GetNode<PlayerInteractionController>("Interaction");
   var provider=new Provider();var target=new InteractionTarget{CollisionLayer=InteractionTarget.Mask,CollisionMask=0,Monitoring=false};
   typeof(InteractionTarget).GetProperty("Provider").SetValue(target,provider);AddChild(target);
   target.AddChild(new CollisionShape2D{Shape=new RectangleShape2D{Size=new Vector2(100,100)}});
   await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
   var resolver=new InteractionResolver();Check(resolver.Resolve(player,Vector2.Zero)==target,"point query selects target");Check(resolver.Resolve(player,new Vector2(1000,1000))==null,"off-art cursor rejects");
   var host=new Node2D{Position=new Vector2(300,0)};AddChild(host);
   var visual=new Node2D{Name="Visual",Position=new Vector2(0,-40)};host.AddChild(visual);
   var image=Image.CreateEmpty(192,144,false,Image.Format.Rgba8);var texture=ImageTexture.CreateFromImage(image);
   visual.AddChild(new Sprite2D{Name="Artwork",Texture=texture,Centered=false,Offset=new Vector2(-96,-108)});
   var worldStorage=new TestWorldStorage{Definition=new StorageDefinition{InteractionRange=500}};host.AddChild(worldStorage);
   typeof(WorldStorage).GetProperty("Host").SetValue(worldStorage,host);
   typeof(WorldStorage).GetProperty("Initialized").SetValue(worldStorage,true);
   InteractionTarget.AttachWhenReady(worldStorage);
   var attached=visual.GetNode<InteractionTarget>("Artwork/InteractionTarget");
   Check(attached.GlobalPosition==new Vector2(300,-76),"runtime hit shape follows sprite and elevation");
   await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
   Check(resolver.Resolve(player,attached.GlobalPosition)==attached,"runtime storage target selectable");
   player.Position=new Vector2(2000,0);Check(!InteractionResolver.Valid(attached,player),"storage reach enforced");player.Position=Vector2.Zero;
   host.Hide();Check(!InteractionResolver.Valid(attached,player),"hidden layer artwork rejected");host.Show();host.QueueFree();
   provider.Eligible=false;Check(resolver.Resolve(player,Vector2.Zero)==null,"range/layer eligibility delegated");provider.Eligible=true;
   Set(controller,"_target",target);
   typeof(PlayerInteractionController).GetMethod("Execute",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);Check(provider.Calls==1,"selected action dispatch");
   provider.Eligible=false;Set(controller,"_target",target);typeof(PlayerInteractionController).GetMethod("Execute",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);Check(provider.Calls==1,"fresh execution recheck");provider.Eligible=true;
   modes.Push(storage,PlayerInputMode.Container);Set(controller,"_target",target);typeof(PlayerInteractionController).GetMethod("Execute",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);Check(provider.Calls==1,"modal priority");modes.Release(storage);
   if(!InputMap.HasAction(PlayerInput.Use)) InputMap.AddAction(PlayerInput.Use); Input.ActionPress(PlayerInput.Use);modes.SuppressWorldUseUntilRelease();Check(modes.GameplayAllowed&&!modes.WorldAttackAllowed,"nonmodal suppression retains movement mode");Check(!modes.WorldAttackAllowed,"held mouse remains blocked");
   Input.ActionRelease(PlayerInput.Use);Check(!modes.WorldAttackAllowed,"release consumed");Check(modes.WorldAttackAllowed,"new use allowed after release");
   target.QueueFree();Check(!InteractionResolver.Valid(target,player),"removed target rejected");
   var wheel=new InteractionRadialHud();AddChild(wheel);wheel.Present(new Vector2(-10,-10),"Interact");Check(wheel.Position.X>=8&&wheel.Position.Y>=8,"wheel clamps viewport");var button=wheel.GetChild<Button>(0);Check(button.ActionMode==BaseButton.ActionModeEnum.Press&&button.Text=="Interact","real clickable primary button");
   GD.Print("PASS: point targeting, eligibility, dispatch recheck, modal priority, held/released LMB guard, removed targets, wheel bounds/button.");GetTree().Quit();
  } catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
 }
}
