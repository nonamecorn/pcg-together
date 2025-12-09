extends Area2D

@export var anim : AnimationPlayer

func _on_body_entered(_body: Node2D) -> void:
	anim.play("hind")


func _on_body_exited(_body: Node2D) -> void:
	
	anim.play_backwards("hind")


func _on_second_tip_body_entered(_body: Node2D) -> void:
	anim.pause()


func _on_second_tip_body_exited(_body: Node2D) -> void:
	anim.pause()
