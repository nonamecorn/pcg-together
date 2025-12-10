extends Node

@onready var stream : AudioStreamPlaybackInteractive = $AudioStreamPlayer.get_stream_playback()

#func _ready() -> void:
	#$AudioStreamPlayer.finished.connect(loop)

var is_calm = false

func switch_track(trackname):
	stream.switch_to_clip_by_name(trackname)

func shift_metal():
	if !is_calm: return
	$AnimationPlayer.play("shift_metal")
	is_calm = false

func shift_calm():
	if is_calm: return
	$AnimationPlayer.play_backwards("shift_metal")
	is_calm = true

#
#func loop():
	#$AudioStreamPlayer.play()
