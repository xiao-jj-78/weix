import time
import unittest
from cloud_bridge import validate_command


class ClientWindowRoutes(unittest.TestCase):
    def command(self, path, body):
        return {'id': 'test_client_route', 'method': 'POST', 'path': path, 'body': body, 'expiresAt': time.time()+60}

    def test_cloud_cannot_select_unbound_pid_or_launch_arbitrary_program(self):
        for path, body in [('/api/sdk/clients/focus', {'instanceId': '42'}), ('/api/sdk/clients/open', {'path': 'cmd.exe'}), ('/api/sdk/clients/verify', {'args': ['anything']})]:
            with self.assertRaises(ValueError): validate_command(self.command(path, body))

    def test_verified_routes_allow_process_identity_but_refuse_control_alias(self):
        identity = '42:638960000000000000'
        self.assertEqual(validate_command(self.command('/api/sdk/clients/focus', {'instanceId': identity}))[1], '/api/sdk/clients/focus')
        with self.assertRaises(ValueError): validate_command(self.command('/api/sdk/clients/alias', {'instanceId': identity, 'alias': '\x80'}))
