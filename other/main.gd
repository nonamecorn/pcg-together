extends Node
signal inventory_changed

var decline_rate := 0.03
@export_range (0.0, 1.0) var player_intencity : float = 0.0
var pause_decline := false
@export var damage_intencity : Curve = Curve.new()
var death_intencity : float = 0.1
var witnesses : Array[int] = []

func add_witness(witness):
	if witnesses.has(witness): return 
	witnesses.append(witness)
	pause_decline = true

func erase_witness(witness):
	witnesses.erase(witness)
	if witnesses.size() == 0:
		pause_decline = false

var inventory : Dictionary = {
	"items": [],
	"gun" : [],
	"helmet": null,
	"bodyarmor": null,
}

func damage(value):
	player_intencity += damage_intencity.sample(value)
	if player_intencity > 1.0:
		player_intencity = 1.0

func death():
	player_intencity += death_intencity
	if player_intencity > 1.0:
		player_intencity = 1.0

func _process(delta: float) -> void:
	if !pause_decline:
		player_intencity = move_toward(player_intencity, 0.0, delta * decline_rate)
	if player_intencity > 0.5:
		OstManager.shift_metal()
	else:
		OstManager.shift_calm()
	


func deal_with_this_shit(item:Item,address:String) -> void:
	if item:
		pass
	inventory[address] = item
	inventory_changed.emit(inventory)

func clear_address(address:String):
	inventory[address] = null
	inventory_changed.emit(inventory)
