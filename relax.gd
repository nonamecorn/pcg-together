extends State

# Called when the node enters the scene tree for the first time.
func enter():
	$Timer.start()

func _on_timer_timeout() -> void:
	transitioned.emit("BuildUp")
