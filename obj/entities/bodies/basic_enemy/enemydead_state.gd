extends DeathState

@export var perceptor : Perceptor
@export var ray : RayCast2D
@export var gun : Node2D
func enter():
	super.enter()
	perceptor.queue_free()
	ray.queue_free()
	gun.state = 1
	main.death()
