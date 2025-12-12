extends Polygon2D

@export var group = ""

func _physics_process(_delta):
	if get_tree().get_nodes_in_group(group).size() != 0:
		global_rotation = global_position.angle_to_point(get_tree().get_nodes_in_group(group)[0].global_position)
	 
