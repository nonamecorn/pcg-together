extends Node

@export var tilemap : TileMapLayer

@export var juk : PackedScene
@export var robot : PackedScene
@export var player : PackedScene

var player_inst

var cells : Array
var cell_size := 32.0



func _ready() -> void:
	cells = tilemap.get_used_cells_by_id(0, Vector2i(0,0), 0)
	spawn_player()
	for cell in cells:
		for i in range(randi_range(0,1)):
			spawn_at_random(cell)


func spawn_player():
	cells.shuffle()
	player_inst = player.instantiate()
	player_inst.global_position = Vector2(cells[0]) * cell_size
	get_tree().current_scene.add_child.call_deferred(player_inst)

func spawn_at_random(cell_coords : Vector2i):
	print("sr")
	var offset_vector = Vector2i(randi_range(-3,3),randi_range(-3,3))
	var enemy_inst
	if randf() > 0.3:
		enemy_inst = juk.instantiate()
	else:
		enemy_inst = robot.instantiate()
	enemy_inst.global_position = Vector2(cell_coords + offset_vector) * cell_size
	get_tree().current_scene.add_child.call_deferred(enemy_inst)

func spawn_wave():
	print("sw")
	var player_pos = tilemap.map_to_local(player_inst.global_position)
	for cell in nearest_cells(player_pos):
		spawn_mob(cell, player_pos)

func spawn_mob(cell_coords : Vector2i, player_cell : Vector2i):
	var offset_vector = Vector2i(randi_range(-3,3),randi_range(-3,3))
	var enemy_inst
	if randf() > 0.3:
		enemy_inst = juk.instantiate()
	else:
		enemy_inst = robot.instantiate()
	enemy_inst.global_position = (cell_coords + offset_vector) * cell_size
	get_tree().current_scene.add_child(enemy_inst)
	enemy_inst.mob(Vector2(player_cell) * cell_size)

func nearest_cells(player_cell):
	cells.sort_custom(func(a, b): return a.distance_to(player_cell) < b.distance_to(player_cell))
	print(cells.slice(0, 4))
	return cells.slice(0, 4)


	

	

	
	

	
