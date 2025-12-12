extends Entity

func _on_hitbox_damaged(damage: float, damage_vec : Vector2) -> void:
	super(damage, damage_vec)
	main.damage(damage)

func _process(delta: float) -> void:
	hp = move_toward(hp, MAX_HP, delta)
