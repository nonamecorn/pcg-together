extends State

@export var inventory : Control

func enter():
	inventory.show()
func exit():
	inventory.return_item()
	inventory.hide()
func unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		transitioned.emit(self,"walk")

#func physics_update(_delta):
	#if Input.is_action_just_pressed("ui_cancel"):
		#transitioned.emit(self,"walk")
