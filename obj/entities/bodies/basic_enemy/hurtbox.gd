extends Area2D

@warning_ignore("unused_signal")
signal loaded
var state = FIRE
enum {
	FIRE,
	STOP,
}
func get_pof():
	return global_position

var target : StaticBody2D
@export var damage : float = 10.0
func _on_body_entered(body: Node2D) -> void:
	if body.has_method("hurt"):
		target = body
		$Timer.start()

func _on_timer_timeout() -> void:
	if state == STOP:
		$Timer.stop()
		return
	target.hurt(damage, target.global_position - global_position)

func _on_body_exited(body: Node2D) -> void:
	if body == target:
		stop_fire()

func stop_fire():
	pass
