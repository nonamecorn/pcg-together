extends State

@export var spawner : Node


func enter():
	$Timer.start()

func exit():
	$Timer.stop()

func _on_timer_timeout() -> void:
	print("huh")
	spawner.spawn_wave()
	if main.player_intencity > 0.9:
		transitioned.emit("SustainPeak")
	
