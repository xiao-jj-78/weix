import unittest
from unittest.mock import Mock
from console_app.sdk_bridge import SdkBridge


class ClientManagementRoutes(unittest.TestCase):
    def setUp(self):
        self.bridge = SdkBridge()
        self.bridge._request = Mock(return_value=({'ok': True}, 200))

    def test_pid_reuse_requires_full_process_identity_before_focus(self):
        for body in ({'instanceId': '42'}, {'instanceId': '42:638960000000000000', 'command': 'anything'}, {'instanceId': '../42:638960000000000000'}):
            self.assertEqual(self.bridge.proxy('POST', '/api/sdk/clients/focus', body)[1], 400)
        self.bridge._request.assert_not_called()
        self.assertEqual(self.bridge.proxy('POST', '/api/sdk/clients/focus', {'instanceId': '42:638960000000000000'})[1], 200)

    def test_alias_is_bounded_and_verify_does_not_accept_arbitrary_sdk_arguments(self):
        for body in ({'instanceId': '42:638960000000000000', 'alias': 'x' * 61}, {'instanceId': '42:638960000000000000', 'alias': 'line\nbreak'}):
            self.assertEqual(self.bridge.proxy('POST', '/api/sdk/clients/alias', body)[1], 400)
        self.assertEqual(self.bridge.proxy('POST', '/api/sdk/clients/verify', {'pid': 42})[1], 400)
        self.bridge._request.assert_not_called()
        self.bridge.proxy('POST', '/api/sdk/clients/verify', {})
        self.assertEqual(self.bridge._request.call_args.kwargs['timeout'], 105)
