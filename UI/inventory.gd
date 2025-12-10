extends Control

const item_base = preload("res://UI/item_base.tscn")

var item_held = null
var item_offset = Vector2()
var last_container = null
var last_pos = Vector2()

#signal drop(item)
#signal money_changed

var current_reroll_cost = 10

var default_text = "..."
var popup_items = []

func _ready() -> void:
	pickup_item(load("res://res/guns/BR180.tres").duplicate())
func _physics_process(_delta):
	if !visible: 
		return
	var cursor_pos = get_global_mouse_position()
	if Input.is_action_just_pressed("l_click"):
		grab(cursor_pos)
	elif Input.is_action_just_released("l_click"):
		release(cursor_pos)
	if item_held != null:
		item_held.global_position = cursor_pos - item_held.get_global_rect().size/2 #+ item_offset
 
func grab(cursor_pos):
	var c = get_container_under_cursor(cursor_pos)
	if c != null and c.has_method("grab_item"):
		item_held = c.grab_item(cursor_pos)
		if item_held != null:
			last_container = c
			last_pos = item_held.global_position
			item_offset = item_held.global_position - cursor_pos
			$Items.move_child(item_held,-1)
 
func release(cursor_pos):
	if item_held == null:
		return
	var c = get_container_under_cursor(cursor_pos)
	if c == null:
		return_item()
	elif c.has_method("insert_item"):
		if c.insert_item(item_held):
			item_held = null
		else:
			return_item()
	else:
		return_item()

func get_container_under_cursor(cursor_pos):
	var containers = get_tree().get_nodes_in_group("Container")
	var active_containers = containers.filter(func(thing): return thing.visible)
	for c in active_containers:
		if c.get_global_rect().has_point(cursor_pos):
			return c
	return null

func return_item():
	if !item_held: return
	item_held.global_position = last_pos
	if last_container.has_method("insert_item"):
		last_container.insert_item(item_held)
	item_held = null

func pickup_item(item_res : Item):
	var item = item_base.instantiate()
	item.custom_minimum_size = item_res.sprite.get_size() * 4
	item.item_resource = item_res
	item.texture = item_res.sprite
	$Items.add_child(item)
	if !$Backpack.insert_item_at_first_available_spot(item):
		item.queue_free()
		return false
	#$audio/grab.play()
	return true
