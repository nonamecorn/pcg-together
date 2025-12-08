extends Resource

class_name Item

@export var item_name : String = ""
@export_multiline var item_description : String = ''
@export var type : String = ""
@export var ammount : int = 1
@export var max_stack : int = 1
@export var cost : int = 5
@export var sprite : Texture2D
@export var sprite_offset : Vector2
@export var weight : float = 1.0
@export var scene : PackedScene
