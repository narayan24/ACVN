extends Node
class_name KitchenService

## Calculates the required number of actions to prepare the provided list of item ids.
func _calc_actions(items: Array) -> int:
    var total_actions := 0
    for item_id in items:
        total_actions += ItemDB.prep_actions(item_id)
    return max(1, total_actions)
