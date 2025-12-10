extends Node2D
class_name Gun

@export var default_modules : Dictionary[String,Node2D]
@export var max_spread: float = 1.0
@export var min_spread: float = 0.0
@export var max_ammo: int = 1
@export var num_of_pellets: int = 1
@export var bullet_obj: PackedScene
@export var brass_texture: Texture
@export var ver_recoil: float
@export var hor_recoil: float
@export var lifetime: float = 1.0
@export var noise_radius: float = 500.0
@export var anim_firerate: float = 1.0
#var gun_resourcesd
var spread_tween
@onready var rng = RandomNumberGenerator.new()
@export var player_handled = false
var ammo
var brass_obj = preload("res://components/brass.tscn")
var spread = 0
var added_velocity : Vector2
var offhand : bool = false
var state = FIRE
enum {
	FIRE,
	STOP,
}
#var gpuparticles
var assambled = false
#@export var pitch_shifing : Curve
var firing = false
signal empty
signal loaded
signal ammo_changed(current,max,ind)

#var falloff : Curve
#var firing_strategies = []
#var bullet_strategies = []

#var silenced = false

#var enabled : bool = false

#func enable():
	#state = FIRE
#func disable():
	#state = STOP

#func get_current_animation_length():
	#current_animation_length

func _ready() -> void:
	#if current_animation_length > 0.225:
		#speed_scale = current_animation_length
	$BANG.set_radius(noise_radius)
	ammo = max_ammo
	spread = min_spread
	rng.randomize()

func get_pof() -> Vector2:
	return $POF.global_position

func _process(_delta: float) -> void:
	if !player_handled: return
	if !offhand:
		if Input.is_action_just_pressed("l_click"):
			start_fire()
		if Input.is_action_just_released("l_click"):
			stop_fire()
	else:
		if Input.is_action_just_pressed("r_click"):
			start_fire()
		if Input.is_action_just_released("r_click"):
			stop_fire()
	if Input.is_action_just_pressed("reload"):
		reload()

func add_part(node_path : NodePath):
	var node = load(node_path)
	var node_inst = node.instantiate()
	$modules.find_child(node_inst.type).add_child(node_inst)

func reset_part(part_name):
	var slot = $modules.find_child(part_name)
	if slot.get_child_count() == 1:
		slot.get_child(0).queue_free()
	if slot is Marker2D:
		var node_inst = default_modules[part_name].instantiate()
		slot.add_child(node_inst)

#func dispawn_facade(part_name):
	#var slot = find_child(part_name)
	#if slot.get_child_count() == 0: return
	#for child in slot.get_children():
		#child.queue_free()
	#slot.position = Vector2.ZERO

func reset_spread():
	spread = min_spread
	if spread_tween: spread_tween.kill()

func start_fire():
	if state: return
	if ammo <= 0:
		empty.emit()
		#if player_handled: ^/out_of_ammo.play()
		return
	if !$AnimationPlayer.is_playing():
		$AnimationPlayer.play("fire")
	firing = true
	if spread_tween: spread_tween.kill()
	spread_tween = create_tween()
	spread_tween.tween_property(self, "spread", max_spread, anim_firerate*max_ammo)

func stop_fire():
	if state:
		return
	firing = false
	if spread_tween: spread_tween.kill()
	spread_tween = create_tween()
	spread_tween.tween_property(self, "spread", min_spread, anim_firerate*max_ammo)
	#gpuparticles.emitting = false

func _on_reload_timeout():
	stop_fire()
	ammo = max_ammo
	#if player_handled: ^/reload_end_cue.play()
	#$MAG.show()
	state = FIRE
	loaded.emit()
	display_ammo()

func reload():
	if ammo == max_ammo: return
	stop_fire()
	state = STOP
	if player_handled:
		ammo = 0
		display_ammo()
		#^/reload_start_cue.play()
	#$reload.start()
	$AnimationPlayer.play("reload")
	spread = min_spread

#func wear_down():
	#for part in gun_resources:
		#if !gun_resources[part]: continue
		#gun_resources[part].curr_durability -= wear

#func weapon_functional():
	#for part in gun_resources:
		#if !gun_resources[part]: continue
		#if gun_resources[part].curr_durability <= 0:
			#gun_resources[part].destry_item()
			#return false
	#return true

func display_ammo():
	ammo_changed.emit(ammo,max_ammo,get_index())

func get_pitch() -> float:
	return rng.randf_range(1.0,1.1)

func fire():
	if state: return
	
	for i in num_of_pellets:
		if ammo <= 0:
			#gpuparticles.emitting = false
			$AnimationPlayer.stop()
			empty.emit()
			return
		ammo -= 1
		display_ammo()
		#wear_down()
		#if !silenced:
			#$AnimationPlayer.play("fire")
			#^/shoting.pitch_scale = get_pitch()
			#^/shoting.play()
		#else:
			#^/silenced_shooting.pitch_scale = get_pitch()
			#^/silenced_shooting.play()
		$audio/fire.pitch_scale = get_pitch()
		$BANG.bang()
		var bullet_inst = bullet_obj.instantiate()
		bullet_inst.global_rotation_degrees = global_rotation_degrees + rng.randf_range(-spread, spread)
		added_velocity = get_parent().get_parent().get_parent().velocity
		#bullet_inst.falloff = falloff
		#bullet_inst.max_range = the_range     /////do this one!!!
		#for strategy in bullet_strategies:
			#bullet_inst.strategies.append(strategy)
		#for strategy in firing_strategies:
			#strategy.apply_strategy(bullet_inst, self)
		bullet_inst.mod_vec = added_velocity
		bullet_inst.lifetime = lifetime
		get_tree().current_scene.call_deferred("add_child",bullet_inst)
		bullet_inst.global_position = get_pof()
		bullet_inst.global_rotation_degrees = global_rotation_degrees  + randf_range(-spread, spread)
		
		var recoil_vector = Vector2(-ver_recoil,randf_range(-hor_recoil, hor_recoil))
		get_parent().get_parent().cursor.apply_recoil(recoil_vector)
	
	#firing = true
	#$single_shot.start()
	muzzle_flash()
	#await $single_shot.timeout
	#firing = false
	
	#if !weapon_functional():
		#^/something_broke.play()
		#display_ammo()
		#stop_fire()


#func _on_single_shot_timeout() -> void:
	#gpuparticles.emitting = false

func eject_brass():
	var brass_inst = brass_obj.instantiate()
	brass_inst.global_position = $ejector.global_position
	brass_inst.global_rotation = global_rotation + rng.randf_range(-PI/8, PI/8)
	brass_inst.get_child(0).texture = brass_texture
	added_velocity = get_parent().get_parent().get_parent().velocity/2
	get_tree().current_scene.call_deferred("add_child",brass_inst)
	#brass_inst.init(added_velocity, lifetime)
func eject_mag():
	var brass_inst = brass_obj.instantiate()
	brass_inst.global_position = $MAG.global_position
	brass_inst.global_rotation = global_rotation + rng.randf_range(-PI/8, PI/8) - PI/2
	brass_inst.get_child(0).texture = $MAG.texture
	brass_inst.velocity_range = [200, 300] 
	added_velocity = get_parent().get_parent().get_parent().velocity/2
	get_tree().current_scene.call_deferred("add_child",brass_inst)
	#brass_inst.init(added_velocity, lifetime)
func muzzle_flash():
	var muzzle_inst = preload("res://components/muzzle_flash.tscn").instantiate()
	muzzle_inst.global_position = get_pof()
	muzzle_inst.global_rotation = global_rotation
	get_tree().current_scene.call_deferred("add_child",muzzle_inst)

func _on_animation_player_animation_finished(anim_name: StringName) -> void:
	if firing and anim_name == "fire":
		$AnimationPlayer.play("fire")
	if anim_name == "reload":
		_on_reload_timeout()
