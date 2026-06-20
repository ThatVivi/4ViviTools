import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from patterns import sample_value, ROLE_PATTERNS

def test_every_role_samples_a_string():
    for role in ROLE_PATTERNS:
        s = sample_value(role)
        assert isinstance(s, str) and len(s) > 0

def test_hp_is_two_numbers_with_slash():
    assert "/" in sample_value("HP")
