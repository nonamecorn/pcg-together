extends Control

var loaded_item: Node
@export var slot : String = "gun"
@export var item_address : String:
	set(value):
		item_address = value
		if !loaded_item or item_address == "": return
		main.clear_address(item_address)
		main.deal_with_this_shit(loaded_item.item_resource, value)
@export var offhand : bool = false
func _ready() -> void:
	resized.connect(recenter_item)

func _on_insert_item(_item):
	pass

func insert_item(item) -> bool:
	#var item_pos = item.global_position + item.size / 2
	var item_slot = item.item_resource.type
	if item_slot != slot:
		return false
	if loaded_item != null:
		return false
	loaded_item = item
	item.global_position = global_position + size / 2 - item.size / 2
	_on_insert_item(item)
	main.deal_with_this_shit(item.item_resource, item_address)
	
	return true

func recenter_item():
	if !loaded_item: return
	loaded_item.global_position = global_position + size / 2 - loaded_item.size / 2

func occupied():
	if loaded_item != null:
		return true
	return false

func _on_grab_item(_pos):
	pass

func grab_item(_pos):
	var item = loaded_item
	if item == null:
		return null
	loaded_item = null
	main.clear_address(item_address)
	_on_grab_item(item)
	return item
 
func get_thing_under_pos(arr, pos):
	for thing in arr:
		if thing != null and thing.get_global_rect().has_point(pos):
			return thing
	return null

func destroy_item():
	var item = grab_item(Vector2.ZERO)
	item.item_resource.destroy.disconnect(destroy_item)
	item.queue_free()
