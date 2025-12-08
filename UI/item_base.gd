extends TextureRect

@export var item_resource : Item:
	set(value):
		item_resource = value
		update_ammount()

func update_ammount():
	$Label.text = ""
	if item_resource.ammount > 1:
		$Label.text = str(item_resource.ammount)

func hide_NPR():
	$NinePatchRect.hide()

func show_NPR():
	$NinePatchRect.show()
