extends State

class_name DeathState

@export var entity : Entity
@export var sprite : Sprite2D
@export var hitbox_coll : CollisionShape2D
@export var wall_coll : CollisionShape2D
@export var hand : Node2D
@export var animation : AnimationPlayer
@export var handmarker : Marker2D
func enter():
	entity.velocity = Vector2.ZERO
	sprite.rotate(PI/2)
	hitbox_coll.disabled = true
	wall_coll.disabled = true
	hand.hide()
	animation.play("idle")
	if handmarker.get_child_count() != 0:
		handmarker.get_child(0).state = 1
	
