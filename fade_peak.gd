extends State

func enter():
	$Timer.start()

func exit():
	$Timer.stop()

func _on_timer_timeout() -> void:
	if main.player_intencity < 0.2:
		transitioned.emit("relax")
