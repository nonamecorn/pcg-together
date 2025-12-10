extends State

@export var inventory : Control
@export var animation : AnimationPlayer
@export var handmarker : Marker2D
func enter():
	animation.play("idle")
	if handmarker.get_child_count() != 0:
		handmarker.get_child(0).state = 1
	inventory.show()
func exit():
	inventory.return_item()
	if handmarker.get_child_count() != 0:
		handmarker.get_child(0).state = 0
	inventory.hide()
func unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		transitioned.emit(self,"walk")

#func physics_update(_delta):
	#if Input.is_action_just_pressed("ui_cancel"):
		#transitioned.emit(self,"walk")
