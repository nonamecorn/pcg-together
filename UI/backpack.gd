extends GridContainer

var items = []
 
var grid = {}
@export var cell_size : float = 64.0

var grid_width : int = 0
var grid_height : int = 0


func _ready():
	grid_width = columns - 1
	grid_height = int(get_child_count()/columns)
	for x in columns:
		grid[x] = {}
		for y in range(int(get_child_count()/columns)):
			grid[x][y] = false



func insert_item(item):
	var item_pos = item.global_position + Vector2(cell_size / 2.0, cell_size / 2.0)
	var g_pos = pos_to_grid_coord(item_pos)
	var item_size = get_grid_size(item)
	if is_grid_space_available(g_pos.x, g_pos.y, item_size.x, item_size.y):
		set_grid_space(g_pos.x, g_pos.y, item_size.x, item_size.y, true)
		item.global_position = global_position + Vector2(g_pos.x, g_pos.y) * cell_size
		item.show_NPR()
		items.append(item)
		return true
	elif can_stack(get_item_under_pos(item_pos), item):
		var item_under = get_item_under_pos(item_pos)
		item_under.item_resource.ammount += item.item_resource.ammount
		item_under.update_ammount()
		item_under.show_NPR()
		item.queue_free()
		return true
	else:
		return false

func can_stack(item1, item2) -> bool:
	if item1.item_resource.item_name != item2.item_resource.item_name:
		return false
	if item1.item_resource.ammount + item2.item_resource.ammount > item1.item_resource.max_stack:
		return false
	return true

func pos_to_grid_coord(pos):
	var local_pos = pos - global_position
	var results = {}
	results.x = int(local_pos.x / cell_size)
	results.y = int(local_pos.y / cell_size)
	return results

func get_grid_size(item):
	var results = {}
	var s = item.size
	results.x = clamp(int(s.x / cell_size), 1, 500)
	results.y = clamp(int(s.y / cell_size), 1, 500)
	return results

func is_grid_space_available(x, y, w ,h):
	if x < 0 or y < 0:
		return false
	if x + w > columns or y + h > (get_child_count()/columns):
		return false
	for i in range(x, x + w):
		for j in range(y, y + h):
			if grid[i][j]:
				return false
	return true

func set_grid_space(x, y, w, h, state):
	for i in range(x, x + w):
		for j in range(y, y + h):
			grid[i][j] = state
 
func get_item_under_pos(pos):
	for item in items:
		if item.get_global_rect().has_point(pos):
			return item
	return null

func grab_item(pos):
	var item = get_item_under_pos(pos)
	if item == null:
		return null
	var item_pos = item.global_position + Vector2(cell_size / 2, cell_size / 2)
	var g_pos = pos_to_grid_coord(item_pos)
	var item_size = get_grid_size(item)
	set_grid_space(g_pos.x, g_pos.y, item_size.x, item_size.y, false)
	items.pop_at(items.find(item))
	item.hide_NPR()
	return item

func insert_item_at_first_available_spot(item):
	for y in range(grid_height):
		for x in range(grid_width):
			if !grid[x][y]:
				item.global_position = global_position + Vector2(x, y) * cell_size
				if insert_item(item):
					return true
	return false
